using Silk.NET.OpenXR;
using Ryujinx.Common.Logging;
using Silk.NET.Core;
using SPB.Graphics.OpenGL;
using SPB.Windowing;
using System.Runtime.InteropServices;
using System.Text;


namespace Ryujinx.VR
{
    public static class RyuXR
    {
        public static XR xr;

        private static ApplicationInfo appInfo;
        private const string APP_NAME = "Ryujinx";
        private const string ENG_NAME = "Custom";

        public static XRContext ctx;



        public unsafe static bool InitializeXR(OpenGLContextBase context, SwappableNativeWindowBase window)
        {
            xr = XR.GetApi();

            CreateXRInstance();

            SystemGetInfo systemGetInfo = new SystemGetInfo(StructureType.TypeSystemGetInfo);
            systemGetInfo.FormFactor = FormFactor.HeadMountedDisplay;
            ulong systemID = 0;
            xr.GetSystem(ctx.instance, &systemGetInfo, &systemID);


            SystemProperties systemProperties = new SystemProperties(StructureType.TypeSystemProperties);

            xr.GetSystemProperties(ctx.instance, systemID, &systemProperties);
            string systemName = "";
            for (int i = 0; i < XR.MaxSystemNameSize; i++)
            {
                if (systemProperties.SystemName[i] == 0) break;
                systemName += (char)systemProperties.SystemName[i];
            }

            Logger.Info?.Print(LogClass.VR,
                "System \"" + systemName + "\" has max layers " + systemProperties.GraphicsProperties.MaxLayerCount +
                " with max swapchain image size " + systemProperties.GraphicsProperties.MaxSwapchainImageWidth + "x" +
                systemProperties.GraphicsProperties.MaxSwapchainImageHeight);

            //Switch only ever uses stereo in games. If it's unsupported, by the current runtime, throw an error for now.
            ctx.views =
                EnumerateAndReturnAvailableViewConfigViews(systemID, ViewConfigurationType.PrimaryStereo);
            LogViewConfigurationViewInfo(ctx.views);

            GraphicsRequirementsOpenGLKHR glkhr =
                new GraphicsRequirementsOpenGLKHR(StructureType.TypeGraphicsRequirementsOpenglKhr);
            PfnVoidFunction pfnVoidFunction = new PfnVoidFunction();
            xr.GetInstanceProcAddr(ctx.instance, "xrGetOpenGLGraphicsRequirementsKHR", ref pfnVoidFunction);
            Delegate xrGetOpenGLGraphicsRequirementsKHR =
                Marshal.GetDelegateForFunctionPointer((IntPtr)pfnVoidFunction.Handle,
                    typeof(Definitions.pfnGetOpenGLGraphicsRequirementsKHR));
            xrGetOpenGLGraphicsRequirementsKHR.DynamicInvoke(ctx.instance, systemID, new IntPtr(&glkhr));
            string minVersion = ((UInt16)((glkhr.MinApiVersionSupported >> 48) & 0xffffUL)) + "." +
                                ((UInt16)((glkhr.MinApiVersionSupported >> 32) & 0xffffUL)) + "." +
                                ((UInt16)((glkhr.MinApiVersionSupported >> 16) & 0xffffUL));
            string maxVersion = ((UInt16)((glkhr.MaxApiVersionSupported >> 48) & 0xffffUL)) + "." +
                                ((UInt16)((glkhr.MaxApiVersionSupported >> 32) & 0xffffUL)) + "." +
                                ((UInt16)((glkhr.MaxApiVersionSupported >> 16) & 0xffffUL));
            Logger.Info?.Print(LogClass.VR,
                "OpenGL Version Min: " + minVersion + ", Max: " +
                maxVersion); // so much more code to write to also support vulkan pls merge soon so i can fetch and implement :((

            GraphicsBinding graphicsBinding;

            if (OperatingSystem.IsWindows())
            {
                graphicsBinding = new GraphicsBindingOpenGLWin32KHR(StructureType.TypeGraphicsBindingOpenglWin32Khr)
                {
                    HGlrc = context.ContextHandle, HDC = window.DisplayHandle.RawHandle
                };
            }
            else if (OperatingSystem.IsLinux())
            {
                graphicsBinding = new GraphicsBindingOpenGLXlibKHR(StructureType.TypeGraphicsBindingOpenglXlibKhr)
                {
                    GlxContext = context.ContextHandle,
                    GlxDrawable = window.WindowHandle.RawHandle,
                    XDisplay = (nint*)window.DisplayHandle.RawHandle.ToPointer()
                };

            }
            else
            {
                // I dont have the capacity to test anything that isn't Windows or Linux, nor am i sure that any other options (FreeBSD, OSX, etc.) have any support at all for OpenXR. Someone should get on that.
                throw new NotImplementedException(
                    "Your operating system is not supported by our OpenXR Implementation! Please open an issue on https://github.com/Ryujinx/Ryujinx with your operating system if one does not already exist!");
            }

            SessionCreateInfo sessionCreateInfo = new SessionCreateInfo(StructureType.TypeSessionCreateInfo);
            sessionCreateInfo.Next = new IntPtr(&graphicsBinding).ToPointer();
            sessionCreateInfo.SystemId = systemID;
            ctx.session = new Session();

            xr.CreateSession(ctx.instance, &sessionCreateInfo, ref ctx.session);

            ReferenceSpaceCreateInfo referenceSpaceCreateInfo =
                new ReferenceSpaceCreateInfo(StructureType.TypeReferenceSpaceCreateInfo)
                {
                    Next = null,
                    PoseInReferenceSpace = new Posef(new Quaternionf(0f, 0f, 0f, 1f), new Vector3f(0f, 0f, 0f)),
                    ReferenceSpaceType = ReferenceSpaceType.Local
                };
            ctx.space = new Space();
            xr.CreateReferenceSpace(ctx.session, referenceSpaceCreateInfo, ref ctx.space);
            Logger.Info?.Print(LogClass.VR, "System ID: " + systemID);

            SessionBeginInfo sessionBeginInfo = new SessionBeginInfo(StructureType.TypeSessionBeginInfo)
            {
                Next = null, PrimaryViewConfigurationType = ViewConfigurationType.PrimaryStereo
            };
            xr.BeginSession(ctx.session, sessionBeginInfo);

            uint swapchainFormatCount = 0;
            xr.EnumerateSwapchainFormats(ctx.session, 0, ref swapchainFormatCount, null);
            long[] formats = new long[swapchainFormatCount];

            fixed (long* formatFirst = formats)
                xr.EnumerateSwapchainFormats(ctx.session, swapchainFormatCount, ref swapchainFormatCount, formatFirst);

            ctx.swapchains = new List<Swapchain>();
            ctx.images = new List<SwapchainImageBaseHeader>();

            for (int i = 0; i < ctx.views.Count; i++)
            {
                Swapchain swapchain = new Swapchain();
                SwapchainCreateInfo swapchainCreateInfo =
                    new SwapchainCreateInfo(StructureType.TypeSwapchainCreateInfo)
                    {
                        UsageFlags =
                            SwapchainUsageFlags.SwapchainUsageSampledBit |
                            SwapchainUsageFlags.SwapchainUsageColorAttachmentBit,
                        CreateFlags = 0,
                        ArraySize = 1,
                        FaceCount = 1,
                        Format = formats[0],
                        Height = ctx.views[i].RecommendedImageRectHeight,
                        Width = ctx.views[i].RecommendedImageRectWidth,
                        SampleCount = ctx.views[i].RecommendedSwapchainSampleCount,
                        MipCount = 0,
                        Next = null,
                    };
                xr.CreateSwapchain(ctx.session, &swapchainCreateInfo, &swapchain);
                ctx.swapchains.Add(swapchain);

                uint swapchainLength = 0;
                xr.EnumerateSwapchainImages(ctx.swapchains[i], 0, ref swapchainLength, null);

                Logger.Info?.Print(LogClass.VR, "Swapchain length for view " + i + " is " + swapchainLength);

                SwapchainImageBaseHeader swapchainImageBaseHeader =
                    new SwapchainImageBaseHeader(StructureType.TypeSwapchainImageOpenglKhr);
                xr.EnumerateSwapchainImages(ctx.swapchains[i], swapchainLength, ref swapchainLength,
                    ref swapchainImageBaseHeader);
                uint* pointer = (uint*) &swapchainImageBaseHeader;
                SwapchainImageOpenGLKHR openGlImage = *((SwapchainImageOpenGLKHR*)pointer); //this is so hacky but it works LMFAO
                ctx.images.Add(openGlImage);

            }

            return true;
        }

        private static unsafe void CreateXRInstance()
        {
            List<string> extensions = EnumerateAndReturnAvailableInstanceExtensions();
            
            Logger.Info?.Print(LogClass.VR, "Available Extensions:");
            for(int i = 0; i < extensions.Count; i++)
            {
                Logger.Info?.Print(LogClass.VR, extensions[i]);
            }

            if (!extensions.Contains("XR_KHR_opengl_enable"))
            {
                Logger.Error?.Print(LogClass.VR, "Not all required XR extensions are available!");
            }

            InstanceCreateInfo instanceCreateInfo = new InstanceCreateInfo(StructureType.TypeInstanceCreateInfo);

            fixed (ApplicationInfo* applicationInfo = &appInfo)
            {
                for (int i = 0; i < APP_NAME.Length; i++)
                {
                    applicationInfo->ApplicationName[i] = (byte)APP_NAME[i];
                }

                for (int i = 0; i < ENG_NAME.Length; i++)
                {
                    applicationInfo->EngineName[i] = (byte)ENG_NAME[i];
                }

                applicationInfo->ApiVersion = 0x1000000000000;

            }
            
            byte[][] requested = new byte[1][];
            requested[0] = new byte[XR.MaxExtensionNameSize];
            byte[] openglenablebytes = Encoding.ASCII.GetBytes("XR_KHR_opengl_enable");

            for (int i = 0; i < openglenablebytes.Length; i++)
            {
                requested[0][i] = openglenablebytes[i];
            }

            fixed (byte* bytePointer = &requested[0][0])
            {
                byte*[] arrayofptr = new byte*[requested.Length];
                for (int i = 0; i < requested.Length; i++)
                    fixed (byte* ptr = &requested[i][0])
                    {
                        arrayofptr[i] = ptr;
                    }
                fixed (byte** req = &arrayofptr[0])
                {
                    instanceCreateInfo.ApplicationInfo = appInfo;
                    instanceCreateInfo.EnabledExtensionCount = 1;
                    instanceCreateInfo.EnabledExtensionNames = req;
                    ctx.instance = new Instance();
                    xr.CreateInstance(instanceCreateInfo, ref ctx.instance);
                }
            }
        }

        private static void LogViewConfigurationViewInfo(List<ViewConfigurationView> views)
        {
            for (int i = 0; i < views.Count; i++)
            {
                Logger.Info?.Print(LogClass.VR, "Resolution: ");
                Logger.Info?.Print(LogClass.VR, "   Reccomended: " + views[i].RecommendedImageRectWidth + "x" + views[i].RecommendedImageRectHeight);
                Logger.Info?.Print(LogClass.VR, "   Max: " + views[i].MaxImageRectWidth + "x" + views[i].MaxImageRectHeight);
                Logger.Info?.Print(LogClass.VR, "Swapchain Samples: ");
                Logger.Info?.Print(LogClass.VR, "   Reccomended: " + views[i].RecommendedSwapchainSampleCount);
                Logger.Info?.Print(LogClass.VR, "   Max: " + views[i].MaxSwapchainSampleCount);
            }
        }

        static unsafe List<ViewConfigurationView> EnumerateAndReturnAvailableViewConfigViews(ulong systemID, ViewConfigurationType viewType)
        {
            List<ViewConfigurationView> views = new List<ViewConfigurationView>();
            uint viewCount = 0;
            xr.EnumerateViewConfigurationView(ctx.instance, systemID, viewType, 0, &viewCount, null);
            fixed (ViewConfigurationView* viewConfigViews = new ViewConfigurationView[viewCount])
            {
                for (int i = 0; i < viewCount; i++)
                {
                    viewConfigViews[i] = new ViewConfigurationView(StructureType.TypeViewConfigurationView);
                }

                xr.EnumerateViewConfigurationView(ctx.instance, systemID, viewType, viewCount, ref viewCount, viewConfigViews);
                for (int i = 0; i < viewCount; i++)
                {
                    views.Add(viewConfigViews[i]);
                }
            }
            return views;
        }
        
        static unsafe List<string> EnumerateAndReturnAvailableInstanceExtensions()
        {
            uint propertyNum = 0;
            xr.EnumerateInstanceExtensionProperties((byte*) null, 0, ref propertyNum, null);
            List<String> extensions = new List<string>();
            fixed (ExtensionProperties* properties = new ExtensionProperties[propertyNum])
            {
                for (int i = 0; i < propertyNum; i++)
                {
                    properties[i] = new ExtensionProperties(StructureType.TypeExtensionProperties);
                }
                xr.EnumerateInstanceExtensionProperties((byte*)null, propertyNum, ref propertyNum, properties);
                for (int i = 0; i < propertyNum; i++)
                {
                    string propertyName = "";
                    for (int j = 0; j < XR.MaxExtensionNameSize; j++)
                    {
                        if (properties[i].ExtensionName[j] == 0) break;
                        propertyName += (char)properties[i].ExtensionName[j];
                    }
                    extensions.Add(propertyName);
                }
            }

            return extensions;
        }

        public static unsafe bool Frame()
        {
            uint viewCount = (uint) ctx.views.Count;

            
            FrameState frameState = new FrameState(StructureType.TypeFrameState);
            FrameWaitInfo frameWaitInfo = new FrameWaitInfo(StructureType.TypeFrameWaitInfo);
            xr.WaitFrame(ctx.session, frameWaitInfo, ref frameState);
            
            //some action code would go here, not working on that yet though.

            FrameBeginInfo frameBeginInfo = new FrameBeginInfo(StructureType.TypeFrameBeginInfo);
            xr.BeginFrame(ctx.session, frameBeginInfo);

            for (int i = 0; i < viewCount; i++)
            {
                SwapchainImageAcquireInfo swapchainImageAcquireInfo =
                    new SwapchainImageAcquireInfo(StructureType.TypeSwapchainImageAcquireInfo);
                uint acquiredIndex = 0;
                xr.AcquireSwapchainImage(ctx.swapchains[i], swapchainImageAcquireInfo, ref acquiredIndex);

                (ctx.images[i]).Image;
                
                
                Logger.Info?.Print(LogClass.VR, "There should be an image right here");

                SwapchainImageReleaseInfo swapchainImageReleaseInfo =
                    new SwapchainImageReleaseInfo(StructureType.TypeSwapchainImageReleaseInfo);
                xr.ReleaseSwapchainImage(ctx.swapchains[i], swapchainImageReleaseInfo);

            }

            View[] views = new View[viewCount];
            for (int i = 0; i < viewCount; i++)
            {
                views[i] = new View(StructureType.TypeView);
            }

            ViewState viewState = new ViewState(StructureType.TypeViewState);
            ViewLocateInfo viewLocateInfo = new ViewLocateInfo(StructureType.TypeViewLocateInfo)
            {
                DisplayTime = frameState.PredictedDisplayTime,
                Space = ctx.space,
                ViewConfigurationType = ViewConfigurationType.PrimaryStereo
            };
            xr.LocateView(ctx.session, viewLocateInfo, ref viewState, &viewCount, views);
            List < CompositionLayerProjectionView> layerProjectionViews = new List<CompositionLayerProjectionView>();
            for (int i = 0; i < viewCount; i++)
            {
                layerProjectionViews.Add(new CompositionLayerProjectionView(StructureType.TypeCompositionLayerProjectionView)
                {
                    Fov = views[i].Fov,
                    Pose = views[i].Pose,
                    SubImage = new SwapchainSubImage()
                    {
                        Swapchain = ctx.swapchains[i],
                        ImageArrayIndex = 0,
                        ImageRect = new Rect2Di()
                        {
                            Offset = new Offset2Di(0, 0),
                            Extent = new Extent2Di((int)ctx.views[i].RecommendedImageRectWidth, (int)ctx.views[i].RecommendedImageRectHeight)
                        }
                    }
                });
            }

            CompositionLayerProjectionView[] compositionLayerProjectionViews = layerProjectionViews.ToArray();
            CompositionLayerProjection compositionLayerProjection;
            fixed(CompositionLayerProjectionView* first = compositionLayerProjectionViews){
                compositionLayerProjection =
                    new CompositionLayerProjection(StructureType.TypeCompositionLayerProjection)
                    {
                        LayerFlags = 0, 
                        Space = ctx.space, 
                        Views = first, 
                        ViewCount = (uint)compositionLayerProjectionViews.Length
                    };
            }


            CompositionLayerBaseHeader*[] compositionLayerBaseHeaders = new[] {(CompositionLayerBaseHeader* )&compositionLayerProjection};
            FrameEndInfo frameEndInfo;
            fixed(CompositionLayerBaseHeader** first = compositionLayerBaseHeaders){
                frameEndInfo = new FrameEndInfo(StructureType.TypeFrameEndInfo)
                {
                    DisplayTime = frameState.PredictedDisplayTime,
                    EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
                    Layers = first,
                    LayerCount = 1
                };
            }
            xr.EndFrame(ctx.session, frameEndInfo);
            
            return true;
        }
    }
}