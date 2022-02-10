using Ryujinx.Common.Configuration;
using Silk.NET.OpenXR;
using Ryujinx.Common.Logging;
using Ryujinx.Configuration;
using Ryujinx.Modules;


namespace Ryujinx.VR
{
    class RyuXR
    {
        public static string ConfigurationPath { get; set; }
        public static XR api;

        public unsafe static bool InitializeXR(XR api)
        {
            List<string> extensions = EnumerateAndReturnAvailableInstanceExtensions(api);
            
            Logger.Info?.PrintMsg(LogClass.VR, "Available Extensions:");
            for(int i = 0; i < extensions.Count; i++)
            {
                Logger.Info?.PrintMsg(LogClass.VR, extensions[i]);
            }

            if (!extensions.Contains("XR_KHR_opengl_enable"))
            {
                Logger.Error?.PrintMsg(LogClass.VR, "Not all required XR extensions are available!");
            }

            InstanceCreateInfo instanceCreateInfo = new InstanceCreateInfo(StructureType.TypeInstanceCreateInfo);
            Instance xrInstance = new Instance();

            api.CreateInstance(instanceCreateInfo, ref xrInstance);

            return true;
        }

        static unsafe List<string> EnumerateAndReturnAvailableInstanceExtensions(XR api)
        {
            uint propertyNum = 0;
            api.EnumerateInstanceExtensionProperties((byte*) null, 0, ref propertyNum, null);
            List<String> extensions = new List<string>();
            fixed (ExtensionProperties* properties = new ExtensionProperties[propertyNum])
            {
                for (int i = 0; i < propertyNum; i++)
                {
                    properties[i] = new ExtensionProperties(StructureType.TypeExtensionProperties);
                }
                Result result = api.EnumerateInstanceExtensionProperties((byte*)null, propertyNum, ref propertyNum, properties);
                for (int i = 0; i < propertyNum; i++)
                {
                    string propertyName = "";
                    for (int j = 0; j < XR.MaxExtensionNameSize; j++)
                    {
                        propertyName += (char)properties[i].ExtensionName[j];
                    }
                    extensions.Add(propertyName);
                }
            }

            return extensions;
        }
    }
}