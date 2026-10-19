using System;

namespace FratRow.Social
{
    public static class SocialModelCatalog
    {
        public const string QwenFile = "Qwen_Qwen3.5-2B-Q4_K_M.gguf";
        public const string QwenHash = "57a1085840f497d764a7fc5d346922dbde961efb54cc792ea81d694fd846a1d8";
        public const string MiniFile = "MiniCPM5-1B-Q4_K_M.gguf";
        public const string MiniHash = "81b64d05a23b17b34c475f42b3e72fbde62d4b92cc34541f7a8031d0752deafa";
        public const string Qwen4File = "Qwen_Qwen3.5-4B-Q4_K_M.gguf";
        public const string Qwen4Hash = "13c16f426047e2de38cd075bdade4a7bcbc8c774384876f677740cda65f8a983";
        public static string HashFor(string filename)
        {
            if (filename == QwenFile) return QwenHash;
            if (filename == MiniFile) return MiniHash;
            if (filename == Qwen4File) return Qwen4Hash;
            throw new ArgumentException("Model is not in the pinned experiment catalog");
        }
    }
}
