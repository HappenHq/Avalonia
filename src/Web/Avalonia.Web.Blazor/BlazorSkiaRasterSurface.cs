using System.Runtime.InteropServices;
using Avalonia.Controls.Platform.Surfaces;
using Avalonia.Platform;
using Avalonia.Skia;
using SkiaSharp;

namespace Avalonia.Web.Blazor
{
    internal class BlazorSkiaRasterSurface : IBlazorSkiaSurface, IFramebufferPlatformSurface
    {
        public SKColorType ColorType { get; set; }

        public PixelSize Size { get; set; }

        public double Scaling { get; set; }

        private LockedFramebuffer? _fb;
        private byte[]? _fbData;
        private GCHandle _fbDataHandle;
        private readonly Action<IntPtr, SKSizeI> _blitCallback;
        private readonly Action _onDisposeAction;

        public BlazorSkiaRasterSurface(
            SKColorType colorType, PixelSize size, double scaling, Action<IntPtr, SKSizeI> blitCallback)
        {
            ColorType = colorType;
            Size = size;
            Scaling = scaling;
            _blitCallback = blitCallback;
            _onDisposeAction = Blit;
        }

        public ILockedFramebuffer Lock()
        {
            var bytesPerPixel = 4; // TODO: derive from ColorType
            var dpi = Scaling * 96.0;
            var width = (int)(Size.Width * Scaling);
            var height = (int)(Size.Height * Scaling);

            if (_fb is null || _fb.Size.Width != width || _fb.Size.Height != height)
            {
                FreeFramebufferData();
                AllocateFramebufferData(width * height * bytesPerPixel);
            }

            var rowBytes = Size.Width * bytesPerPixel;
            var pixelFormat = ColorType.ToPixelFormat();
            return _fb = new LockedFramebuffer(
                _fbDataHandle.AddrOfPinnedObject(), new PixelSize(width, height), rowBytes,
                new Vector(dpi, dpi), pixelFormat, _onDisposeAction);
        }

        private void AllocateFramebufferData(int size)
        {
            _fbData = new byte[size];
            _fbDataHandle = GCHandle.Alloc(_fbData, GCHandleType.Pinned);
        }

        private void FreeFramebufferData()
        {
            if (_fbData != null)
            {
                _fbDataHandle.Free();
                _fbData = null;
            }
        }

        private void Blit()
        {
            if (_fb != null)
            {
                _blitCallback(_fb.Address, new SKSizeI(_fb.Size.Width, _fb.Size.Height));
            }
        }
    }
}
