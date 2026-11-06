using System;
using System.IO;

using Lennox.NvEncSharp;
using Vortice.Direct3D11;

using static Lennox.NvEncSharp.LibNvEnc;

internal static class NvencTextureProbe
{
    public static void EncodeThirtyFrames(
        ID3D11Device device,
        ID3D11DeviceContext context,
        ID3D11Texture2D texture,
        string outputPath)
    {
        NvEncoder encoder = default;
        NvEncCreateBitstreamBuffer bitstream = default;

        int encodedFrames = 0;
        long encodedBytes = 0;

        try
        {
            Console.WriteLine();
            Console.WriteLine("===== NVENC TEXTURE ENCODING =====");

            // Open NVENC using the SAME D3D11 device
            // used by the compositor.
            encoder = OpenEncoderForDirectX(device.NativePointer);

            if (encoder.Handle == IntPtr.Zero)
                throw new InvalidOperationException(
                    "NVENC returned an invalid session handle.");

            Console.WriteLine("[PASS] NVENC DirectX session opened.");

            var desc = texture.Description;

            if (desc.Width != 1920 || desc.Height != 1080)
                throw new InvalidOperationException(
                    $"Unexpected composite size: {desc.Width}x{desc.Height}");

            var config = encoder.GetEncodePresetConfigEx(
                NvEncCodecGuids.H264,
                NvEncPresetGuids.P1).PresetCfg;

            config.FrameIntervalP = 1;
            config.RcParams.EnableLookahead = false;
            config.RcParams.AverageBitRate = 8_000_000;
            config.RcParams.MaxBitRate = 12_000_000;
            config.RcParams.RateControlMode = NvEncParamsRcMode.Vbr;

            unsafe
            {
                NvEncConfig* configPointer = &config;

                var init = new NvEncInitializeParams
                {
                    Version = NV_ENC_INITIALIZE_PARAMS_VER,

                    EncodeGuid = NvEncCodecGuids.H264,

                    EncodeWidth = 1920,
                    EncodeHeight = 1080,

                    MaxEncodeWidth = 1920,
                    MaxEncodeHeight = 1080,

                    DarWidth = 1920,
                    DarHeight = 1080,

                    FrameRateNum = 30,
                    FrameRateDen = 1,

                    PresetGuid = NvEncPresetGuids.P1,

                    EnableEncodeAsync = 0,
                    EnablePTD = 1,

                    EncodeConfig = configPointer,
                    TuningInfo = NvEncTuningInfo.HighQuality
                };

                encoder.InitializeEncoder(ref init);
            }

            Console.WriteLine("[PASS] H.264 encoder initialized.");

            bitstream = encoder.CreateBitstreamBuffer();

            if (bitstream.BitstreamBuffer.Handle == IntPtr.Zero)
                throw new InvalidOperationException(
                    "NVENC bitstream buffer creation failed.");

            Console.WriteLine("[PASS] Bitstream buffer created.");

            // Register the composite D3D11 texture.
            // D3D11 B8G8R8A8_UNORM maps to NVENC ARGB
            // for the BGRA byte layout used by the sample.
            var registrationParams = new NvEncRegisterResource
            {
                Version = NV_ENC_REGISTER_RESOURCE_VER,

                ResourceType = NvEncInputResourceType.Directx,

                BufferFormat = NvEncBufferFormat.Argb,
                BufferUsage = NvEncBufferUsage.NvEncInputImage,

                ResourceToRegister = texture.NativePointer,

                Width = (uint)desc.Width,
                Height = (uint)desc.Height,

                Pitch = 0
            };

            // Keep registration alive until all frames are encoded.
            // It is disposed before the encoder session is destroyed.
            using (var registration =
                encoder.RegisterResource(ref registrationParams))
            using (var output = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read))
            {
                Console.WriteLine("[PASS] Composite texture registered.");

                for (int frameIndex = 0; frameIndex < 30; frameIndex++)
                {
                    // Submit pending GPU composition commands.
                    context.Flush();

                    var mapped = new NvEncMapInputResource
                    {
                        Version = NV_ENC_MAP_INPUT_RESOURCE_VER,
                        RegisteredResource =
                            registrationParams.RegisteredResource
                    };

                    encoder.MapInputResource(ref mapped);

                    bool isMapped = true;

                    try
                    {
                        var picture = new NvEncPicParams
                        {
                            Version = NV_ENC_PIC_PARAMS_VER,

                            PictureStruct = NvEncPicStruct.Frame,

                            InputBuffer = mapped.MappedResource,
                            BufferFmt = mapped.MappedBufferFmt,

                            InputWidth = 1920,
                            InputHeight = 1080,

                            OutputBitstream = bitstream.BitstreamBuffer,

                            // Unique monotonically increasing timestamps.
                            InputTimeStamp = (ulong)(frameIndex * 33),
                            InputDuration = 33
                        };

                        encoder.EncodePicture(ref picture);

                        using (var encodedStream =
                            encoder.LockBitstreamAndCreateStream(
                                ref bitstream))
                        {
                            long before = output.Position;

                            encodedStream.CopyTo(output);

                            long written = output.Position - before;

                            if (written <= 0)
                                throw new InvalidOperationException(
                                    $"No bitstream data returned for frame {frameIndex}.");

                            encodedBytes += written;
                        }

                        encodedFrames++;
                    }
                    finally
                    {
                        if (isMapped)
                            encoder.UnmapInputResource(
                                mapped.MappedResource);
                    }
                }

                output.Flush(true);
            }

            if (encodedFrames != 30 || encodedBytes <= 0)
                throw new InvalidOperationException(
                    "Incomplete NVENC output.");

            Console.WriteLine("[PASS] Composite texture unregistered.");
            Console.WriteLine($"[PASS] Encoded frames: {encodedFrames}");
            Console.WriteLine($"[PASS] Encoded bytes: {encodedBytes}");
            Console.WriteLine($"[PASS] H.264 elementary stream: {outputPath}");
        }
        finally
        {
            if (encoder.Handle != IntPtr.Zero)
            {
                try
                {
                    if (bitstream.BitstreamBuffer.Handle != IntPtr.Zero)
                    {
                        encoder.DestroyBitstreamBuffer(
                            bitstream.BitstreamBuffer);
                    }
                }
                finally
                {
                    encoder.DestroyEncoder();
                }
            }
        }
    }
}
