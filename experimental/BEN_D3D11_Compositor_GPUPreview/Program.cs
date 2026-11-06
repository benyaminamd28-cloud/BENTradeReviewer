using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.Mathematics;

using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

internal static class Program
{
    private const int OutputWidth = 1920;
    private const int OutputHeight = 1080;

    private const int DxgiErrorWaitTimeout =
        unchecked((int)0x887A0027);

    private const string ShaderSource = @"
Texture2D Tex0 : register(t0);
Texture2D Tex1 : register(t1);
Texture2D Tex2 : register(t2);

SamplerState LinearSampler : register(s0);

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 UV : TEXCOORD0;
};

VSOutput VSMain(uint vertexId : SV_VertexID)
{
    float2 position;

    if (vertexId == 0)
        position = float2(-1.0, 1.0);
    else if (vertexId == 1)
        position = float2(3.0, 1.0);
    else
        position = float2(-1.0, -3.0);

    VSOutput output;
    output.Position = float4(position, 0.0, 1.0);
    output.UV = float2(
        (position.x + 1.0) * 0.5,
        (1.0 - position.y) * 0.5
    );

    return output;
}

float4 PSMain(VSOutput input) : SV_TARGET
{
    float2 uv = input.UV;

    // DISPLAY1: upper-left, 960 x 540.
    if (uv.x < 0.5 && uv.y < 0.5)
    {
        return Tex0.Sample(
            LinearSampler,
            float2(uv.x * 2.0, uv.y * 2.0)
        );
    }

    // DISPLAY2: upper-right, 960 x 540.
    if (uv.x >= 0.5 && uv.y < 0.5)
    {
        return Tex1.Sample(
            LinearSampler,
            float2((uv.x - 0.5) * 2.0, uv.y * 2.0)
        );
    }

    // DISPLAY3: lower-center, 960 x 540.
    if (uv.y >= 0.5 && uv.x >= 0.25 && uv.x < 0.75)
    {
        return Tex2.Sample(
            LinearSampler,
            float2((uv.x - 0.25) * 2.0, (uv.y - 0.5) * 2.0)
        );
    }

    // Unused canvas area.
    return float4(0.0, 0.0, 0.0, 1.0);
}
";

    private sealed class CaptureSlot : IDisposable
    {
        public string DeviceName { get; }
        public int DisplayNumber { get; }

        public IDXGIOutputDuplication Duplication { get; }

        public ID3D11Texture2D? Texture { get; set; }
        public ID3D11ShaderResourceView? ShaderView { get; set; }

        public CaptureSlot(
            string deviceName,
            int displayNumber,
            IDXGIOutputDuplication duplication)
        {
            DeviceName = deviceName;
            DisplayNumber = displayNumber;
            Duplication = duplication;
        }

        public void Dispose()
        {
            ShaderView?.Dispose();
            Texture?.Dispose();
            Duplication.Dispose();
        }
    }

    private static int Main()
    {
        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("FATAL ERROR");
            Console.WriteLine(ex);
            return 1;
        }
    }

    private static int Run()
    {
        Console.WriteLine("BEN D3D11 GPU COMPOSITOR PREVIEW");
        Console.WriteLine("================================");

        using IDXGIFactory1 factory =
            CreateDXGIFactory1<IDXGIFactory1>();

        IDXGIAdapter1? selectedAdapter = null;

        // Select the NVIDIA hardware adapter.
        for (uint ai = 0; ; ai++)
        {
            var result = factory.EnumAdapters1(
                ai,
                out IDXGIAdapter1? candidate);

            if (result.Failure || candidate is null)
                break;

            var description = candidate.Description1;

            Console.WriteLine(
                $"Adapter: {description.Description}, " +
                $"Vendor=0x{description.VendorId:X4}");

            if (description.VendorId == 0x10DE &&
                (description.Flags & AdapterFlags.Software) == 0)
            {
                selectedAdapter = candidate;
                break;
            }

            candidate.Dispose();
        }

        if (selectedAdapter is null)
            throw new InvalidOperationException(
                "NVIDIA hardware adapter not found.");

        using (selectedAdapter)
        {
            var featureLevels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0
            };

            var deviceResult = D3D11CreateDevice(
                selectedAdapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out ID3D11Device device,
                out FeatureLevel featureLevel,
                out ID3D11DeviceContext context);

            if (deviceResult.Failure)
                throw new InvalidOperationException(
                    $"D3D11 device creation failed: {deviceResult}");

            using (device)
            using (context)
            {
                Console.WriteLine(
                    $"[PASS] D3D11 Feature Level: {featureLevel}");

                var slots = new List<CaptureSlot>();

                try
                {
                    // Create all three duplication interfaces.
                    for (uint oi = 0; ; oi++)
                    {
                        var outputResult =
                            selectedAdapter.EnumOutputs(
                                oi,
                                out IDXGIOutput? output);

                        if (outputResult.Failure || output is null)
                            break;

                        using (output)
                        using (IDXGIOutput1 output1 =
                            output.QueryInterface<IDXGIOutput1>())
                        {
                            var description = output.Description;
                            string name = description.DeviceName;

                            var match = Regex.Match(
                                name,
                                @"DISPLAY(?<number>\d+)$",
                                RegexOptions.IgnoreCase);

                            if (!match.Success)
                                throw new InvalidOperationException(
                                    $"Cannot identify display number: {name}");

                            int displayNumber = int.Parse(
                                match.Groups["number"].Value);

                            IDXGIOutputDuplication duplication =
                                output1.DuplicateOutput(device);

                            slots.Add(new CaptureSlot(
                                name,
                                displayNumber,
                                duplication));

                            Console.WriteLine(
                                $"[PASS] Duplication created: {name}");
                        }
                    }

                    slots = slots
                        .OrderBy(x => x.DisplayNumber)
                        .ToList();

                    if (slots.Count != 3 ||
                        slots.Select(x => x.DisplayNumber).Distinct().Count() != 3)
                    {
                        throw new InvalidOperationException(
                            $"Expected 3 unique outputs, found {slots.Count}.");
                    }

                    // Capture one frame from each monitor.
                    // Copy to persistent GPU texture before ReleaseFrame.
                    foreach (var slot in slots)
                    {
                        CaptureOneFrame(device, context, slot);

                        string sourcePath = Path.Combine(
                            AppContext.BaseDirectory,
                            $"SOURCE_DISPLAY{slot.DisplayNumber}.bmp");

                        SaveDiagnosticBitmap(
                            device,
                            context,
                            slot.Texture!,
                            sourcePath);

                        Console.WriteLine(
                            $"[DIAG] Source image saved: {sourcePath}");
                    }

                    Console.WriteLine();
                    Console.WriteLine("[PASS] Three persistent GPU textures ready.");

                    // Compile the GPU shaders.
                    ReadOnlyMemory<byte> vertexBytecode =
                        Compiler.Compile(
                            ShaderSource,
                            "VSMain",
                            "BENComposite.hlsl",
                            "vs_5_0");

                    ReadOnlyMemory<byte> pixelBytecode =
                        Compiler.Compile(
                            ShaderSource,
                            "PSMain",
                            "BENComposite.hlsl",
                            "ps_5_0");

                    using ID3D11VertexShader vertexShader =
                        device.CreateVertexShader(vertexBytecode.Span);

                    using ID3D11PixelShader pixelShader =
                        device.CreatePixelShader(pixelBytecode.Span);

                    Console.WriteLine("[PASS] Vertex shader compiled.");
                    Console.WriteLine("[PASS] Pixel shader compiled.");

                    // Create 1920 x 1080 GPU render target.
                    using ID3D11Texture2D outputTexture =
                        device.CreateTexture2D(
                            Format.B8G8R8A8_UNorm,
                            OutputWidth,
                            OutputHeight,
                            bindFlags: BindFlags.RenderTarget);

                    using ID3D11RenderTargetView renderTarget =
                        device.CreateRenderTargetView(outputTexture);

                    using ID3D11SamplerState sampler =
                        device.CreateSamplerState(
                            SamplerDescription.LinearClamp);

                    // Bind inputs and render target.
                    ID3D11ShaderResourceView[] shaderResources =
                        slots.Select(x => x.ShaderView!).ToArray();

                    context.OMSetRenderTargets(renderTarget);

                    context.RSSetViewport(
                        0,
                        0,
                        OutputWidth,
                        OutputHeight);

                    context.ClearRenderTargetView(
                        renderTarget,
                        new Color4(0f, 0f, 0f, 1f));

                    context.IASetInputLayout(null);
                    context.IASetPrimitiveTopology(
                        PrimitiveTopology.TriangleList);

                    context.VSSetShader(vertexShader);
                    context.PSSetShader(pixelShader);

                    context.PSSetShaderResources(
                        0,
                        shaderResources);

                    context.PSSetSamplers(
                        0,
                        new[] { sampler });

                    // Three monitor textures are composited by the GPU.
                    context.Draw(3, 0);
                    context.Flush();

                                        string h264Path = Path.Combine(
                        AppContext.BaseDirectory,
                        "BEN_Composite_1920x1080.h264");

                    NvencTextureProbe.EncodeThirtyFrames(
                        device,
                        context,
                        outputTexture,
                        h264Path);
Console.WriteLine(
                        "[PASS] GPU shader composition executed.");

                    // Diagnostic-only readback for visual validation.
                    // The production capture/encode path will not use this.
                    string bitmapPath = Path.Combine(
                        AppContext.BaseDirectory,
                        "BEN_Composite_1920x1080.bmp");

                    SaveDiagnosticBitmap(
                        device,
                        context,
                        outputTexture,
                        bitmapPath);

                    // Independent render-target/readback test.
                    context.ClearRenderTargetView(
                        renderTarget,
                        new Color4(0f, 1f, 0f, 1f));

                    context.Flush();

                    string greenPath = Path.Combine(
                        AppContext.BaseDirectory,
                        "TEST_SOLID_GREEN.bmp");

                    SaveDiagnosticBitmap(
                        device,
                        context,
                        outputTexture,
                        greenPath);

                    Console.WriteLine(
                        $"[DIAG] Solid green image saved: {greenPath}");

                    Console.WriteLine();
                    Console.WriteLine("COMPOSITION TEST COMPLETE");
                    Console.WriteLine($"Resolution: {OutputWidth}x{OutputHeight}");
                    Console.WriteLine($"Layout: 2 + 1");
                    Console.WriteLine($"Preview: {bitmapPath}");

                    return 0;
                }
                finally
                {
                    foreach (var slot in slots)
                        slot.Dispose();
                }
            }
        }
    }

    private static void CaptureOneFrame(
        ID3D11Device device,
        ID3D11DeviceContext context,
        CaptureSlot slot)
    {
        const int attempts = 5;
        const uint timeoutMilliseconds = 2000;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            IDXGIResource? desktopResource = null;
            bool acquired = false;

            try
            {
                var result = slot.Duplication.AcquireNextFrame(
                    timeoutMilliseconds,
                    out var frameInfo,
                    out desktopResource);

                if (result.Failure)
                {
                    if (result.Code == DxgiErrorWaitTimeout &&
                        attempt < attempts)
                    {
                        Console.WriteLine(
                            $"Waiting for {slot.DeviceName} frame...");

                        continue;
                    }

                    throw new InvalidOperationException(
                        $"AcquireNextFrame failed for {slot.DeviceName}: {result}");
                }

                acquired = true;

                // DXGI can report a pointer-only update without
                // updating the desktop bitmap.
                // Never treat such a frame as a valid desktop image.
                if (frameInfo.AccumulatedFrames == 0 ||
                    frameInfo.LastPresentTime == 0)
                {
                    Console.WriteLine(
                        $"[SKIP] {slot.DeviceName}: pointer-only/no desktop update. " +
                        $"AccumulatedFrames={frameInfo.AccumulatedFrames}, " +
                        $"LastPresentTime={frameInfo.LastPresentTime}, " +
                        $"LastMouseUpdateTime={frameInfo.LastMouseUpdateTime}");

                    continue;
                }

                Console.WriteLine(
                    $"[FRAME] {slot.DeviceName}: " +
                    $"AccumulatedFrames={frameInfo.AccumulatedFrames}, " +
                    $"LastPresentTime={frameInfo.LastPresentTime}, " +
                    $"ProtectedContentMaskedOut={frameInfo.ProtectedContentMaskedOut}");

                using (desktopResource!)
                using (ID3D11Texture2D sourceTexture =
                    desktopResource!.QueryInterface<ID3D11Texture2D>())
                {
                    var desc = sourceTexture.Description;

                    if (desc.Format != Format.B8G8R8A8_UNorm)
                    {
                        throw new NotSupportedException(
                            $"{slot.DeviceName} returned unexpected format: " +
                            $"{desc.Format}");
                    }

                    if (desc.Width != 1920 || desc.Height != 1080)
                    {
                        throw new InvalidOperationException(
                            $"{slot.DeviceName} unexpected dimensions: " +
                            $"{desc.Width}x{desc.Height}");
                    }

                    if (slot.Texture is null)
                    {
                        slot.Texture = device.CreateTexture2D(
                            desc.Format,
                            desc.Width,
                            desc.Height,
                            bindFlags: BindFlags.ShaderResource);

                        slot.ShaderView =
                            device.CreateShaderResourceView(slot.Texture);
                    }

                    // GPU-to-GPU copy; no CPU pixel readback.
                    if (slot.DisplayNumber == 1)
                    {
                        string acquiredPath = Path.Combine(
                            AppContext.BaseDirectory,
                            "DIAG_DISPLAY1_ACQUIRED_SOURCE.bmp");

                        SaveDiagnosticBitmap(
                            device,
                            context,
                            sourceTexture,
                            acquiredPath);

                        Console.WriteLine(
                            $"[DIAG] Acquired source saved: {acquiredPath}");
                    }
                    context.CopyResource(
                        slot.Texture!,
                        sourceTexture);

                    // Diagnostic GPU synchronization:
                    // wait until the persistent texture copy is complete
                    // before releasing the DXGI desktop frame.
                    using (ID3D11Query copyComplete =
                        device.CreateQuery(QueryType.Event))
                    {
                        context.End(copyComplete);
                        context.Flush();

                        var wait = System.Diagnostics.Stopwatch.StartNew();

                        while (!context.IsDataAvailable(copyComplete))
                        {
                            if (wait.ElapsedMilliseconds > 5000)
                            {
                                throw new TimeoutException(
                                    $"GPU copy timeout: {slot.DeviceName}");
                            }

                            System.Threading.Thread.Sleep(1);
                        }

                        Console.WriteLine(
                            $"[PASS] GPU copy completed before ReleaseFrame: {slot.DeviceName}");
                        if (slot.DisplayNumber == 1)
                        {
                            string persistentPath = Path.Combine(
                                AppContext.BaseDirectory,
                                "DIAG_DISPLAY1_PERSISTENT_COPY.bmp");

                            SaveDiagnosticBitmap(
                                device,
                                context,
                                slot.Texture!,
                                persistentPath);

                            Console.WriteLine(
                                $"[DIAG] Persistent copy saved: {persistentPath}");
                        }

                    }
                }

                Console.WriteLine(
                    $"[PASS] Frame copied to persistent GPU texture: " +
                    slot.DeviceName);

                return;
            }
            catch (Exception ex)
            {
                if (ex.HResult == DxgiErrorWaitTimeout &&
                    attempt < attempts)
                    continue;

                throw;
            }
            finally
            {
                desktopResource?.Dispose();

                if (acquired)
                    slot.Duplication.ReleaseFrame().CheckError();
            }
        }

        throw new TimeoutException(
            $"No desktop frame received from {slot.DeviceName}.");
    }

    private static void SaveDiagnosticBitmap(
        ID3D11Device device,
        ID3D11DeviceContext context,
        ID3D11Texture2D outputTexture,
        string path)
    {
        using ID3D11Texture2D staging =
            device.CreateTexture2D(
                Format.B8G8R8A8_UNorm,
                OutputWidth,
                OutputHeight,
                bindFlags: BindFlags.None,
                usage: ResourceUsage.Staging,
                cpuAccessFlags: CpuAccessFlags.Read);

        context.CopyResource(staging, outputTexture);

        var mapped = context.Map(
            staging,
            0,
            MapMode.Read,
            Vortice.Direct3D11.MapFlags.None);

        byte[] pixels = new byte[
            checked(OutputWidth * OutputHeight * 4)];

        int rowBytes = OutputWidth * 4;

        try
        {
            for (int y = 0; y < OutputHeight; y++)
            {
                IntPtr row = IntPtr.Add(
                    mapped.DataPointer,
                    checked(y * (int)mapped.RowPitch));

                Marshal.Copy(
                    row,
                    pixels,
                    y * rowBytes,
                    rowBytes);
            }
        }
        finally
        {
            context.Unmap(staging, 0);
        }

        // 32-bit top-down BMP. DXGI BGRA byte order is compatible.
        int imageSize = pixels.Length;
        int fileSize = checked(54 + imageSize);

        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0x4D42); // BM
        writer.Write(fileSize);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(54);

        writer.Write(40);             // BITMAPINFOHEADER
        writer.Write(OutputWidth);
        writer.Write(-OutputHeight);  // top-down image
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);              // BI_RGB
        writer.Write(imageSize);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        writer.Write(pixels);
        writer.Flush();
    }
}






