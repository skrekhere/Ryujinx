using Silk.NET.OpenXR;
using Ryujinx.Common.Logging;
using Silk.NET.Core;
using System.Drawing.Printing;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;


namespace Ryujinx.VR
{
    public static class RyuXR
    {
        public static XR xr;

        private static ApplicationInfo appInfo;
        private const string APP_NAME = "Ryujinx";
        private const string ENG_NAME = "Custom";

        private static Instance instance;



        public unsafe static bool InitializeXR()
        {
            xr = XR.GetApi();

            CreateXRInstance();

            SystemGetInfo systemGetInfo = new SystemGetInfo(StructureType.TypeSystemGetInfo);
            systemGetInfo.FormFactor = FormFactor.HeadMountedDisplay;
            ulong systemID = 0;
            xr.GetSystem(instance, &systemGetInfo, &systemID);


            SystemProperties systemProperties = new SystemProperties(StructureType.TypeSystemProperties);

            xr.GetSystemProperties(instance, systemID, &systemProperties);
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
            List<ViewConfigurationView> views =
                EnumerateAndReturnAvailableViewConfigViews(systemID, ViewConfigurationType.PrimaryStereo);
            LogViewConfigurationViewInfo(views);
            
            GraphicsRequirementsOpenGLKHR glkhr =
                new GraphicsRequirementsOpenGLKHR(StructureType.TypeGraphicsRequirementsOpenglKhr);
            PfnVoidFunction pfnVoidFunction = new PfnVoidFunction();
            xr.GetInstanceProcAddr(instance, "xrGetOpenGLGraphicsRequirementsKHR", ref pfnVoidFunction);
            Delegate xrGetOpenGLGraphicsRequirementsKHR =
                Marshal.GetDelegateForFunctionPointer((IntPtr)pfnVoidFunction.Handle,
                    typeof(Definitions.pfnGetOpenGLGraphicsRequirementsKHR));
            xrGetOpenGLGraphicsRequirementsKHR.DynamicInvoke( instance, systemID, new IntPtr(&glkhr));
            string minVersion = ((UInt16)((glkhr.MinApiVersionSupported >> 48) & 0xffffUL)) + "." + ((UInt16)((glkhr.MinApiVersionSupported >> 32) & 0xffffUL)) + "." + ((UInt16)((glkhr.MinApiVersionSupported >> 16) & 0xffffUL));
            string maxVersion = ((UInt16)((glkhr.MaxApiVersionSupported >> 48) & 0xffffUL)) + "." + ((UInt16)((glkhr.MaxApiVersionSupported >> 32) & 0xffffUL)) + "." + ((UInt16)((glkhr.MaxApiVersionSupported >> 16) & 0xffffUL));
            Logger.Info?.Print(LogClass.VR, "OpenGL Version Min: " + minVersion + ", Max: " + maxVersion); // so much more code to write to also support vulkan pls merge soon so i can fetch and implement :((

            object graphicsBinding;
            
            if (OperatingSystem.IsWindows())
            {
                graphicsBinding = new GraphicsBindingOpenGLWin32KHR(StructureType.TypeGraphicsBindingOpenglWin32Khr);y  

            }else if (OperatingSystem.IsLinux())
            {
                graphicsBinding = new GraphicsBindingOpenGLXlibKHR(StructureType.TypeGraphicsBindingOpenglXlibKhr);

            }else { // I dont have the capacity to test anything that isn't Windows or Linux, nor am i sure that any other options (FreeBSD, OSX, etc.) have any support at all for OpenXR. Someone should get on that.
                throw new NotImplementedException("Your operating system is not supported by our OpenXR Implementation! Please open an issue on https://github.com/Ryujinx/Ryujinx with your operating system if one does not already exist!");
            }
            
            SessionCreateInfo sessionCreateInfo = new SessionCreateInfo(StructureType.TypeSessionCreateInfo);
            sessionCreateInfo.Next = new IntPtr(&graphicsBinding).ToPointer();
            sessionCreateInfo.SystemId = systemID;
            Session session = new Session();



            xr.CreateSession(instance, &sessionCreateInfo, &session);

            Logger.Info?.Print(LogClass.VR, "System ID: " + systemID);

            SessionBeginInfo sessionBeginInfo = new SessionBeginInfo(StructureType.TypeSessionBeginInfo);
            
            xr.BeginSession(session, &sessionBeginInfo);

            FrameWaitInfo frameWaitInfo = new FrameWaitInfo(StructureType.TypeFrameWaitInfo);

            FrameState frameState = new FrameState(StructureType.TypeFrameState);

            xr.WaitFrame(session, &frameWaitInfo, &frameState);

            FrameBeginInfo frameBeginInfo = new FrameBeginInfo(StructureType.TypeFrameBeginInfo);

            xr.BeginFrame(session, &frameBeginInfo);

            //xr.CreateSwapchain();

            //xr.AcquireSwapchainImage();
            

            FrameEndInfo frameEndInfo = new FrameEndInfo(StructureType.TypeFrameEndInfo);

            xr.EndFrame(session, &frameEndInfo);

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
                    instance = new Instance();
                    xr.CreateInstance(instanceCreateInfo, ref instance);
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
            xr.EnumerateViewConfigurationView(instance, systemID, viewType, 0, &viewCount, null);
            fixed (ViewConfigurationView* viewConfigViews = new ViewConfigurationView[viewCount])
            {
                for (int i = 0; i < viewCount; i++)
                {
                    viewConfigViews[i] = new ViewConfigurationView(StructureType.TypeViewConfigurationView);
                }

                xr.EnumerateViewConfigurationView(instance, systemID, viewType, viewCount, ref viewCount, viewConfigViews);
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
    }
}