using System;
using System.IO;
using FFmpeg.AutoGen;

namespace ApeFree.DonateScreen.Codecs
{
    /// <summary>
    /// FFmpeg 本地底层动态库环境初始化器
    /// 负责自动定位与挂载 libavcodec、libavformat、libavutil、libswscale 等本地运行库
    /// </summary>
    public static class FFmpegInitializer
    {
        private static readonly object _syncLock = new();
        private static bool _isInitialized = false;

        /// <summary>
        /// FFmpeg 原生库是否已成功初始化
        /// </summary>
        public static bool IsInitialized => _isInitialized;

        static FFmpegInitializer()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// 确保 FFmpeg 原生运行库被正确定位与加载
        /// </summary>
        public static bool EnsureInitialized()
        {
            if (_isInitialized) return true;

            lock (_syncLock)
            {
                if (_isInitialized) return true;

                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";

                    // 1. 优先探测 runtimes/win-x64/native 目录
                    string runtimesNative = Path.Combine(baseDir, "runtimes", arch, "native");
                    if (Directory.Exists(runtimesNative) && File.Exists(Path.Combine(runtimesNative, "avutil-61.dll")) || File.Exists(Path.Combine(runtimesNative, "avcodec-63.dll")))
                    {
                        ffmpeg.RootPath = runtimesNative;
                    }
                    else if (File.Exists(Path.Combine(baseDir, "avcodec-63.dll")) || File.Exists(Path.Combine(baseDir, "avutil-61.dll")))
                    {
                        // 2. 根目录下存在
                        ffmpeg.RootPath = baseDir;
                    }
                    else
                    {
                        // 3. 递归探测常用子目录
                        string archSubDir = Path.Combine(baseDir, arch);
                        if (Directory.Exists(archSubDir))
                        {
                            ffmpeg.RootPath = archSubDir;
                        }
                    }

                    // 验证原生函数调用是否正常
                    uint version = ffmpeg.avcodec_version();
                    if (version > 0)
                    {
                        _isInitialized = true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FFmpegInitializer] 警告: FFmpeg 动态库加载失败: {ex.Message}");
                    _isInitialized = false;
                }

                return _isInitialized;
            }
        }
    }
}
