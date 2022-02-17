using Silk.NET.OpenXR;
using System.Runtime.InteropServices;

namespace Ryujinx.VR;

public class Definitions
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal unsafe delegate Result pfnGetOpenGLGraphicsRequirementsKHR(Instance instance, ulong systemID,
        GraphicsRequirementsOpenGLKHR* requirements);
}