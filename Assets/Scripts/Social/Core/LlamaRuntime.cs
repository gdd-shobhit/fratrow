using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FratRow.Social
{
    /// <summary>The llama.cpp builds the host can launch. All expose the same <c>llama-server</c> HTTP API.</summary>
    public enum LlamaBackend { Cuda, Vulkan, Cpu }

    /// <summary>One installed <c>llama-server</c> build: where it is and how many layers to offload to the GPU.</summary>
    public sealed class LlamaRuntime
    {
        public LlamaBackend Backend;
        public string ServerPath;
        /// <summary>99 offloads every layer (CUDA, Vulkan); 0 keeps the model on the CPU.</summary>
        public int GpuLayers;
        public string Name => "llama.cpp " + LlamaRuntimes.Release + " " + Backend.ToString().ToUpperInvariant();
    }

    /// <summary>
    /// Picks the local inference runtime. llama.cpp is the only one (ADR-0004): CUDA for NVIDIA GPUs, Vulkan for any other
    /// GPU, CPU as the last resort. Builds are installed by <c>tools/social/bootstrap.ps1</c> under
    /// <c>Library/SocialNative/llama-{cuda,vulkan,cpu}</c>. Unity-free so console harnesses and the Unity host share it.
    /// Example: <c>var runtime = LlamaRuntimes.Resolve("Library/SocialNative", LlamaRuntimes.CudaDriverPresent());</c>
    /// </summary>
    public static class LlamaRuntimes
    {
        /// <summary>The pinned llama.cpp release; every build comes from it so behaviour matches across backends.</summary>
        public const string Release = "b11718";
        public const string ServerExe = "llama-server.exe";

        public static string Folder(LlamaBackend backend) =>
            backend == LlamaBackend.Cuda ? "llama-cuda" : backend == LlamaBackend.Vulkan ? "llama-vulkan" : "llama-cpu";

        public static int GpuLayers(LlamaBackend backend) => backend == LlamaBackend.Cpu ? 0 : 99;

        /// <summary>Preference order: CUDA only when it can run, then Vulkan, then CPU.</summary>
        public static IEnumerable<LlamaBackend> Preference(bool cudaCapable) =>
            cudaCapable ? new[] { LlamaBackend.Cuda, LlamaBackend.Vulkan, LlamaBackend.Cpu } : new[] { LlamaBackend.Vulkan, LlamaBackend.Cpu };

        /// <summary>The first installed build in preference order, or null when none is installed under <paramref name="nativeRoot"/>.</summary>
        public static LlamaRuntime Resolve(string nativeRoot, bool cudaCapable, Func<string, bool> exists = null)
        {
            if (string.IsNullOrEmpty(nativeRoot)) return null;
            exists = exists ?? File.Exists;
            foreach (var backend in Preference(cudaCapable))
            {
                string path = Path.Combine(nativeRoot, Folder(backend), ServerExe);
                if (exists(path)) return new LlamaRuntime { Backend = backend, ServerPath = path, GpuLayers = GpuLayers(backend) };
            }
            return null;
        }

        /// <summary>An explicitly chosen server (command line or editor preference). The backend is read from its folder name;
        /// an unrecognised folder is treated as a GPU build.</summary>
        public static LlamaRuntime FromServerPath(string serverPath)
        {
            if (string.IsNullOrWhiteSpace(serverPath)) return null;
            string folder = Path.GetFileName(Path.GetDirectoryName(serverPath) ?? "").ToLowerInvariant();
            var backend = folder.Contains("cpu") ? LlamaBackend.Cpu : folder.Contains("vulkan") ? LlamaBackend.Vulkan : LlamaBackend.Cuda;
            return new LlamaRuntime { Backend = backend, ServerPath = serverPath, GpuLayers = GpuLayers(backend) };
        }

        /// <summary>True on Windows when the NVIDIA driver's CUDA library is installed (nvcuda.dll ships with every NVIDIA
        /// display driver). AMD and Intel machines, and NVIDIA machines without a driver, use Vulkan or CPU instead.</summary>
        public static bool CudaDriverPresent() =>
            Environment.OSVersion.Platform == PlatformID.Win32NT && File.Exists(Path.Combine(Environment.SystemDirectory, "nvcuda.dll"));

        /// <summary>Builds installed under <paramref name="nativeRoot"/>, for diagnostics and editor menus.</summary>
        public static IEnumerable<LlamaBackend> Installed(string nativeRoot, Func<string, bool> exists = null)
        {
            exists = exists ?? File.Exists;
            return new[] { LlamaBackend.Cuda, LlamaBackend.Vulkan, LlamaBackend.Cpu }
                .Where(b => !string.IsNullOrEmpty(nativeRoot) && exists(Path.Combine(nativeRoot, Folder(b), ServerExe)));
        }
    }
}
