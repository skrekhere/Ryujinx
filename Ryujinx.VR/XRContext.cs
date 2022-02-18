using Silk.NET.OpenXR;

namespace Ryujinx.VR;

public struct XRContext
{
    public Instance instance;
    public Session session;
    public SessionState state;
    public ulong systemID;

    public List<ViewConfigurationView> views;
    
    public Space space;

    public List<Swapchain> swapchains;
    public List<SwapchainImageOpenGLKHR> images; //for now- making it exclusive to OpenGL. makes testing easier. this will be compatible with vulkan too i promise :)
}