using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;

namespace SilkWebGpuPbr;

public static unsafe class Program
{
    private const TextureFormat DepthFormat = TextureFormat.Depth24Plus;

    private static IWindow _window = null!;
    private static WebGPU _wgpu = null!;
    private static Wgpu _wgpuExt = null!;
    private static Instance* _instance;
    private static Surface* _surface;
    private static Adapter* _adapter;
    private static Device* _device;
    private static Queue* _queue;
    private static SurfaceConfiguration _surfaceConfiguration;
    private static TextureFormat _surfaceFormat;
    private static Texture* _depthTexture;
    private static TextureView* _depthView;

    private static ChunkRenderer? _chunkRenderer;
    private static WorldManager? _worldManager;
    private static double _time;

    public static void Main()
    {
        WindowOptions options = WindowOptions.Default with
        {
            API = GraphicsAPI.None,
            Size = new Vector2D<int>(1280, 720),
            Title = "Silk.NET WebGPU Voxel Engine",
            VSync = true
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Closing += OnClosing;
        _window.Run();
        _window.Dispose();
    }

    private static void OnLoad()
    {
        _wgpu = WebGPU.GetApi();
        _wgpuExt = new Wgpu(_wgpu.Context);

        InstanceDescriptor instanceDescriptor = new();
        _instance = _wgpu.CreateInstance(&instanceDescriptor);
        _surface = WebGPUSurface.CreateWebGPUSurface(_window, _wgpu, _instance);

        _adapter = RequestAdapter();
        _device = RequestDevice(_adapter);
        _queue = _wgpu.DeviceGetQueue(_device);

        ConfigureSurface();
        CreateDepthResources();

        _chunkRenderer = new ChunkRenderer(_wgpu, _device, _queue, _surfaceFormat, DepthFormat);
        _worldManager = new WorldManager(_wgpu, _device, _queue);

        _worldManager.GenerateWorld(6, 2, 6);
    }

    private static Adapter* RequestAdapter()
    {
        InstanceEnumerateAdapterOptions options = new();
        nuint count = _wgpuExt.InstanceEnumerateAdapters(_instance, &options, null);
        if (count == 0)
        {
            throw new InvalidOperationException("No WebGPU adapters were found.");
        }

        Adapter** adapters = stackalloc Adapter*[(int)count];
        _wgpuExt.InstanceEnumerateAdapters(_instance, &options, adapters);
        return adapters[0];
    }

    private static Device* RequestDevice(Adapter* adapter)
    {
        Device* device = null;
        DeviceDescriptor descriptor = new();

        _wgpu.AdapterRequestDevice(adapter, &descriptor, new PfnRequestDeviceCallback(OnDeviceRequested), &device);

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (device is null && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(1);
        }

        return device is null
            ? throw new InvalidOperationException("Timed out while requesting a WebGPU device.")
            : device;
    }

    private static void OnDeviceRequested(RequestDeviceStatus status, Device* device, byte* message, void* userdata)
    {
        if (status != RequestDeviceStatus.Success)
        {
            string error = message is null ? "unknown error" : Marshal.PtrToStringUTF8((nint)message) ?? "unknown error";
            throw new InvalidOperationException($"WebGPU device request failed: {error}");
        }

        *(Device**)userdata = device;
    }

    private static void ConfigureSurface()
    {
        SurfaceCapabilities capabilities = new();
        _wgpu.SurfaceGetCapabilities(_surface, _adapter, &capabilities);
        _surfaceFormat = capabilities.FormatCount > 0 ? capabilities.Formats[0] : _wgpu.SurfaceGetPreferredFormat(_surface, _adapter);

        Vector2D<int> size = _window.FramebufferSize;
        _surfaceConfiguration = new SurfaceConfiguration
        {
            Usage = TextureUsage.RenderAttachment,
            Device = _device,
            Format = _surfaceFormat,
            PresentMode = PresentMode.Fifo,
            AlphaMode = capabilities.AlphaModeCount > 0 ? capabilities.AlphaModes[0] : CompositeAlphaMode.Auto,
            Width = (uint)Math.Max(1, size.X),
            Height = (uint)Math.Max(1, size.Y)
        };

        _wgpu.SurfaceConfigure(_surface, in _surfaceConfiguration);
    }

    private static void CreateDepthResources()
    {
        _depthView = null;
        _depthTexture = null;

        TextureDescriptor descriptor = new()
        {
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(_surfaceConfiguration.Width, _surfaceConfiguration.Height, 1),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = 1,
            Usage = TextureUsage.RenderAttachment
        };

        _depthTexture = _wgpu.DeviceCreateTexture(_device, &descriptor);
        TextureViewDescriptor viewDescriptor = new()
        {
            Format = DepthFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.DepthOnly
        };
        _depthView = _wgpu.TextureCreateView(_depthTexture, &viewDescriptor);
    }

    private static void OnFramebufferResize(Vector2D<int> size)
    {
        if (_surface is null || _device is null || size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        ConfigureSurface();
        CreateDepthResources();
    }

    private static void OnRender(double deltaSeconds)
    {
        if (_chunkRenderer is null || _worldManager is null)
        {
            return;
        }

        _time += deltaSeconds;

        _worldManager.Update();

        SurfaceTexture surfaceTexture = new();
        _wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
        {
            ConfigureSurface();
            return;
        }

        TextureView* colorView = _wgpu.TextureCreateView(surfaceTexture.Texture, null);
        CommandEncoderDescriptor encoderDescriptor = new();
        CommandEncoder* encoder = _wgpu.DeviceCreateCommandEncoder(_device, &encoderDescriptor);

        RenderPassColorAttachment colorAttachment = new()
        {
            View = colorView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color(0.4, 0.6, 0.9, 1.0) // Sky blue
        };

        RenderPassDepthStencilAttachment depthAttachment = new()
        {
            View = _depthView,
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1.0f,
            StencilLoadOp = LoadOp.Undefined,
            StencilStoreOp = StoreOp.Undefined
        };

        RenderPassDescriptor passDescriptor = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
            DepthStencilAttachment = &depthAttachment
        };

        RenderPassEncoder* pass = _wgpu.CommandEncoderBeginRenderPass(encoder, &passDescriptor);

        float radius = 120.0f;
        float camX = MathF.Sin((float)_time * 0.2f) * radius;
        float camZ = MathF.Cos((float)_time * 0.2f) * radius;
        Vector3 cameraPosition = new Vector3(camX + 96, 50 + MathF.Sin((float)_time * 0.1f) * 10, camZ + 96);

        Matrix4x4 view = Matrix4x4.CreateLookAt(cameraPosition, new Vector3(96, 16, 96), Vector3.UnitY);
        float aspect = (float)_surfaceConfiguration.Width / _surfaceConfiguration.Height;
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.0f, aspect, 0.1f, 1000.0f);

        float sunAngle = (float)_time * 0.3f;
        Vector3 lightDir = Vector3.Normalize(new Vector3(MathF.Cos(sunAngle), -0.8f, MathF.Sin(sunAngle)));

        _chunkRenderer.UpdateScene(GpuSceneData.Create(
            view * projection,
            cameraPosition,
            lightDir,
            new Vector3(1.0f, 0.95f, 0.85f),
            3.5f,
            new Vector3(0.4f, 0.5f, 0.7f),
            0.8f
        ));

        _worldManager.Draw(pass, _chunkRenderer);

        _wgpu.RenderPassEncoderEnd(pass);
        CommandBufferDescriptor commandBufferDescriptor = new();
        CommandBuffer* commandBuffer = _wgpu.CommandEncoderFinish(encoder, &commandBufferDescriptor);
        _wgpu.QueueSubmit(_queue, 1, &commandBuffer);
        _wgpu.SurfacePresent(_surface);
    }

    private static void OnClosing()
    {
        _worldManager?.Dispose();
        _chunkRenderer?.Dispose();
    }
}
