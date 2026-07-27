using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace Vape.UI
{
    /// <summary>
    /// Shared memory protocol between Vape.dll (game) and Vape.Overlay.exe.
    /// Layout (256 KB):
    /// [0x00000] Game -> Overlay block (128 KB)
    /// [0x20000] Overlay -> Game block (128 KB)
    /// Each block:
    ///   0..3   magic 'VAPE'
    ///   4..7   version = 1
    ///   8..11  sequence
    ///   12..15 flags
    ///   16..19 payload length
    ///   20..   UTF-8 payload (key=value lines)
    /// </summary>
    public static class OverlaySync
    {
        public const string MapName = "Local\\VapeOverlayIO_v1";
        public const int MapSize = 256 * 1024;
        public const int BlockSize = 128 * 1024;
        public const int HeaderSize = 20;
        public const int Version = 1;
        public static readonly int Magic = BitConverter.ToInt32(Encoding.ASCII.GetBytes("VAPE"), 0);

        public const int FlagMenuOpen = 1 << 0;
        public const int FlagHeartbeat = 1 << 1;
        public const int FlagWantInternalUi = 1 << 2;

        public const int GameBlockOffset = 0;
        public const int OverlayBlockOffset = 128 * 1024;

        public static bool TryOpen(out MemoryMappedFile mmf)
        {
            try
            {
                mmf = MemoryMappedFile.CreateOrOpen(MapName, MapSize, MemoryMappedFileAccess.ReadWrite);
                return true;
            }
            catch
            {
                mmf = null;
                return false;
            }
        }

        public static void WriteBlock(MemoryMappedFile mmf, int offset, int sequence, int flags, string payload)
        {
            if (mmf == null) return;
            byte[] data = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            int max = BlockSize - HeaderSize;
            if (data.Length > max) Array.Resize(ref data, max);

            using (var acc = mmf.CreateViewAccessor(offset, BlockSize, MemoryMappedFileAccess.Write))
            {
                acc.Write(0, Magic);
                acc.Write(4, Version);
                acc.Write(8, sequence);
                acc.Write(12, flags);
                acc.Write(16, data.Length);
                if (data.Length > 0)
                    acc.WriteArray(HeaderSize, data, 0, data.Length);
            }
        }

        public static bool ReadBlock(MemoryMappedFile mmf, int offset, out int sequence, out int flags, out string payload)
        {
            sequence = 0;
            flags = 0;
            payload = string.Empty;
            if (mmf == null) return false;

            using (var acc = mmf.CreateViewAccessor(offset, BlockSize, MemoryMappedFileAccess.Read))
            {
                int magic = acc.ReadInt32(0);
                if (magic != Magic) return false;
                int ver = acc.ReadInt32(4);
                if (ver != Version) return false;
                sequence = acc.ReadInt32(8);
                flags = acc.ReadInt32(12);
                int len = acc.ReadInt32(16);
                if (len < 0 || len > BlockSize - HeaderSize) return false;
                if (len == 0) return true;
                byte[] data = new byte[len];
                acc.ReadArray(HeaderSize, data, 0, len);
                payload = Encoding.UTF8.GetString(data);
                return true;
            }
        }
    }
}
