// Downloads NVIDIA's DLSS and Streamline runtime files into the dlss folder, from NVIDIA's own
// GitHub (https only): the pinned versions below, each file checked against its SHA-256 and its
// Authenticode signature (valid, signed by NVIDIA Corporation) before it is put in place. The
// release itself does not carry NVIDIA's files.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace RyGT
{
    /// FIX-RT-025: a DLL's version strings (CompanyName, FileDescription, ...) read from its PE file
    /// in managed code. Under Proton the launcher runs on Wine Mono, whose FileVersionInfo came back
    /// without NVIDIA's CompanyName and FileDescription, so Add DLSS files refused NVIDIA's own
    /// nvngx_dlss.dll (RyGT issue 79). Returns an empty map for anything it can't read.
    internal static class PeVersionStrings
    {
        public static Dictionary<string, string> Read(string path)
        {
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                byte[] file = File.ReadAllBytes(path);
                byte[] version = VersionResource(file);
                if (version != null) Walk(version, 0, version.Length, strings, 0);
            }
            catch (Exception) { }
            return strings;
        }

        static int U16(byte[] b, int at) { return b[at] | b[at + 1] << 8; }
        static int I32(byte[] b, int at) { return b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24; }

        /// The first RT_VERSION resource's bytes, or null.
        static byte[] VersionResource(byte[] f)
        {
            if (f.Length < 0x40 || U16(f, 0) != 0x5a4d) return null;
            int pe = I32(f, 0x3c);
            if (pe < 0 || pe + 24 > f.Length || I32(f, pe) != 0x4550) return null;
            int sections = U16(f, pe + 6), optional = pe + 24, optionalSize = U16(f, pe + 20);
            int magic = U16(f, optional);
            int dirs = optional + (magic == 0x20b ? 112 : 96);
            if (dirs + 8 * 3 > f.Length) return null;
            int rsrcRva = I32(f, dirs + 8 * 2);
            if (rsrcRva == 0) return null;
            int table = optional + optionalSize;
            Func<int, int> offset = rva =>
            {
                for (int i = 0; i < sections; i++)
                {
                    int s = table + 40 * i;
                    if (s + 40 > f.Length) break;
                    int va = I32(f, s + 12), size = Math.Max(I32(f, s + 8), I32(f, s + 16)), raw = I32(f, s + 20);
                    if (rva >= va && rva < va + size) return raw + (rva - va);
                }
                return -1;
            };
            int root = offset(rsrcRva);
            if (root < 0) return null;
            // Type 16 (RT_VERSION), then its first name and first language.
            int types = Entry(f, root, root, 16);
            if (types < 0) return null;
            int names = Entry(f, root, types, -1);
            if (names < 0) return null;
            int leaf = Entry(f, root, names, -1);
            if (leaf < 0 || leaf + 8 > f.Length) return null;
            int data = offset(I32(f, leaf)), length = I32(f, leaf + 4);
            if (data < 0 || length <= 0 || data + length > f.Length) return null;
            var bytes = new byte[length];
            Buffer.BlockCopy(f, data, bytes, 0, length);
            return bytes;
        }

        /// In the resource directory at `dir`, the offset (from the file start) that entry `id` (or
        /// the first entry for -1) points to.
        static int Entry(byte[] f, int root, int dir, int id)
        {
            if (dir + 16 > f.Length) return -1;
            int count = U16(f, dir + 12) + U16(f, dir + 14);
            for (int i = 0; i < count; i++)
            {
                int e = dir + 16 + 8 * i;
                if (e + 8 > f.Length) return -1;
                int name = I32(f, e), target = I32(f, e + 4);
                if (id >= 0 && ((name & int.MinValue) != 0 || name != id)) continue;
                return root + (target & 0x7fffffff);
            }
            return -1;
        }

        /// VS_VERSIONINFO blocks: wLength, wValueLength, wType, key, padding, value, children.
        /// String blocks (inside StringFileInfo's tables) give the key/value pairs.
        static void Walk(byte[] b, int start, int end, Dictionary<string, string> strings, int depth)
        {
            int at = start;
            while (at + 6 <= end && depth < 4)
            {
                int length = U16(b, at), valueLength = U16(b, at + 2), type = U16(b, at + 4);
                if (length < 6 || at + length > end) return;
                int keyEnd = at + 6;
                while (keyEnd + 1 < at + length && (b[keyEnd] != 0 || b[keyEnd + 1] != 0)) keyEnd += 2;
                string key = Encoding.Unicode.GetString(b, at + 6, keyEnd - (at + 6));
                int value = (keyEnd + 2 + 3) & ~3;
                // A String's value length counts UTF-16 characters (some linkers leave its wType 0).
                int valueBytes = type == 1 || depth == 3 ? valueLength * 2 : valueLength;
                if (depth == 3 && value + valueBytes <= at + length)
                {
                    string text = Encoding.Unicode.GetString(b, value, valueBytes).TrimEnd('\0');
                    if (!strings.ContainsKey(key)) strings[key] = text;
                }
                else if (depth < 3 && (depth != 1 || key == "StringFileInfo"))
                {
                    int children = (value + valueBytes + 3) & ~3;
                    if (children < at + length) Walk(b, children, at + length, strings, depth + 1);
                }
                at = (at + length + 3) & ~3;
            }
        }
    }

    internal static class DlssDownload
    {
        internal const string DlssVersion = "310.9.16";
        internal const string StreamlineVersion = "2.14.16";
        const string DlssBase = "https://raw.githubusercontent.com/NVIDIA/DLSS/v310.9.16/lib/Windows_x86_64/rel/";
        const string StreamlineZip =
            "https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.14.16/streamline-sdk-v2.14.16.zip";

        internal sealed class Pinned
        {
            public string Name, Url, ZipEntry, Sha256;
            public long Size;
        }

        // NVIDIA DLSS v310.9.16 (DLSS SDK) and Streamline v2.14.16 (its release zip's bin/x64).
        internal static readonly Pinned[] Files =
        {
            new Pinned { Name = "nvngx_dlss.dll", Url = DlssBase + "nvngx_dlss.dll", Size = 58956912,
                         Sha256 = "d00b5b0ab29c3ca101b70c3a5ecb3a01b7ef33766a3383a28a18623083e6123e" },
            new Pinned { Name = "nvngx_dlssg.dll", Url = DlssBase + "nvngx_dlssg.dll", Size = 7506544,
                         Sha256 = "8d59d49b36410dd6b62fef2503a5871d347415bd85d943fa0848563a95dd50c7" },
            new Pinned { Name = "sl.interposer.dll", ZipEntry = "bin/x64/sl.interposer.dll", Size = 652928,
                         Sha256 = "b1d7395aebd7b9722841d64dcf7433680020b6a8e421f851c16d5ba0c2182edf" },
            new Pinned { Name = "sl.common.dll", ZipEntry = "bin/x64/sl.common.dll", Size = 843904,
                         Sha256 = "38eea3b4209afc455636ea372b5933d65fcc13becec6c84d101dcfc4d8648f75" },
            new Pinned { Name = "sl.dlss.dll", ZipEntry = "bin/x64/sl.dlss.dll", Size = 422016,
                         Sha256 = "18871eea87c90345da97d8109faa81ea7d5840e8d99b51b55b0e46cfa177f84e" },
            new Pinned { Name = "sl.dlss_g.dll", ZipEntry = "bin/x64/sl.dlss_g.dll", Size = 637056,
                         Sha256 = "3ed9538df724993bea8a050195a6a78b9c8c8a566d5b05ade477aab3c390e90f" },
            new Pinned { Name = "sl.reflex.dll", ZipEntry = "bin/x64/sl.reflex.dll", Size = 388736,
                         Sha256 = "443577e869352436ff7ec8f4d9c90418d3e95368436ce219261e0f4f07a38122" },
            new Pinned { Name = "sl.pcl.dll", ZipEntry = "bin/x64/sl.pcl.dll", Size = 360064,
                         Sha256 = "5698c38e722318681f215c9fe68e45b9bedd51378acc699d8eee26e961e57770" },
            new Pinned { Name = "NvLowLatencyVk.dll", ZipEntry = "bin/x64/NvLowLatencyVk.dll", Size = 57840,
                         Sha256 = "2a77dc3e1c724b7eea5755be0ae7423752e79a2459fae72181a9f00e3507e5d6" },
        };

        internal const string NoticeName = "NVIDIA-NOTICE.txt";

        /// Whether every pinned file in dir is present and matches its hash.
        internal static bool Installed(string dir)
        {
            return Files.All(f => Matches(Path.Combine(dir, f.Name), f));
        }

        static bool Matches(string path, Pinned file)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length != file.Size) return false;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                    return Hex(sha.ComputeHash(stream)) == file.Sha256;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// Downloads what is missing or different into dir. progress gets short status lines.
        /// Returns the number of files downloaded; throws with a readable message on failure.
        internal static int Install(string dir, Action<string> progress)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Directory.CreateDirectory(dir);
            var needed = Files.Where(f => !Matches(Path.Combine(dir, f.Name), f)).ToList();
            ZipIndex zip = null;
            int done = 0;
            foreach (var file in needed)
            {
                progress(string.Format("Downloading {0} ({1} of {2})...", file.Name, done + 1, needed.Count));
                byte[] data;
                if (file.Url != null)
                {
                    data = Get(file.Url, -1, -1);
                }
                else
                {
                    if (zip == null) zip = ZipIndex.Read(StreamlineZip);
                    data = zip.Extract(file.ZipEntry);
                }
                if (data.LongLength != file.Size)
                    throw new InvalidDataException(file.Name + ": unexpected size " + data.LongLength + ".");
                using (var sha = SHA256.Create())
                    if (Hex(sha.ComputeHash(data)) != file.Sha256)
                        throw new InvalidDataException(file.Name + ": SHA-256 does not match the pinned NVIDIA file.");
                string temporary = Path.Combine(dir, file.Name + ".download-" + Guid.NewGuid().ToString("N"));
                try
                {
                    File.WriteAllBytes(temporary, data);
                    CheckNvidiaSignature(temporary, file.Name);
                    string destination = Path.Combine(dir, file.Name);
                    if (File.Exists(destination))
                        File.Replace(temporary, destination,
                                     destination + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".bak");
                    else
                        File.Move(temporary, destination);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                ++done;
            }
            File.WriteAllText(Path.Combine(dir, NoticeName), Notice, Encoding.UTF8);
            return done;
        }

        static byte[] Get(string url, long start, long end)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "ryGT7-launcher";
            request.AllowAutoRedirect = true;
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;
            if (start >= 0) request.AddRange(start, end);
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (!response.ResponseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("NVIDIA's download was redirected away from https.");
                if (start >= 0 && response.StatusCode != HttpStatusCode.PartialContent)
                    throw new InvalidDataException("The download server does not support partial downloads.");
                using (var stream = response.GetResponseStream())
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }

        /// Reads only the zip's central directory, then single entries, with range requests.
        sealed class ZipIndex
        {
            string url;
            readonly Dictionary<string, long[]> entries = new Dictionary<string, long[]>(); // method, csize, usize, offset

            internal static ZipIndex Read(string url)
            {
                var zip = new ZipIndex { url = url };
                long total = Length(url);
                byte[] tail = Get(url, Math.Max(0, total - 65536), total - 1);
                int eocd = LastIndex(tail, new byte[] { 0x50, 0x4b, 0x05, 0x06 });
                if (eocd < 0) throw new InvalidDataException("Streamline's zip has no directory.");
                long cdSize = BitConverter.ToUInt32(tail, eocd + 12);
                long cdOffset = BitConverter.ToUInt32(tail, eocd + 16);
                if (cdOffset == 0xFFFFFFFF)
                {
                    int locator = LastIndex(tail, new byte[] { 0x50, 0x4b, 0x06, 0x07 });
                    long eocd64 = BitConverter.ToInt64(tail, locator + 8);
                    byte[] record = Get(url, eocd64, eocd64 + 55);
                    cdSize = BitConverter.ToInt64(record, 40);
                    cdOffset = BitConverter.ToInt64(record, 48);
                }
                byte[] cd = Get(url, cdOffset, cdOffset + cdSize - 1);
                int pos = 0;
                while (pos + 46 <= cd.Length && BitConverter.ToUInt32(cd, pos) == 0x02014b50)
                {
                    long method = BitConverter.ToUInt16(cd, pos + 10);
                    long csize = BitConverter.ToUInt32(cd, pos + 20), usize = BitConverter.ToUInt32(cd, pos + 24);
                    int nameLength = BitConverter.ToUInt16(cd, pos + 28), extraLength = BitConverter.ToUInt16(cd, pos + 30);
                    int commentLength = BitConverter.ToUInt16(cd, pos + 32);
                    long offset = BitConverter.ToUInt32(cd, pos + 42);
                    string name = Encoding.UTF8.GetString(cd, pos + 46, nameLength).Replace('\\', '/');
                    int extra = pos + 46 + nameLength, extraEnd = extra + extraLength;
                    while (extra + 4 <= extraEnd)
                    {
                        int tag = BitConverter.ToUInt16(cd, extra), size = BitConverter.ToUInt16(cd, extra + 2);
                        if (tag == 1)
                        {
                            int v = extra + 4;
                            if (usize == 0xFFFFFFFF) { usize = BitConverter.ToInt64(cd, v); v += 8; }
                            if (csize == 0xFFFFFFFF) { csize = BitConverter.ToInt64(cd, v); v += 8; }
                            if (offset == 0xFFFFFFFF) { offset = BitConverter.ToInt64(cd, v); }
                        }
                        extra += 4 + size;
                    }
                    zip.entries[name] = new[] { method, csize, usize, offset };
                    pos += 46 + nameLength + extraLength + commentLength;
                }
                return zip;
            }

            internal byte[] Extract(string suffix)
            {
                var match = entries.Keys.FirstOrDefault(k => k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                if (match == null) throw new InvalidDataException("Streamline's zip lacks " + suffix + ".");
                long[] e = entries[match];
                byte[] header = Get(url, e[3], e[3] + 29);
                long start = e[3] + 30 + BitConverter.ToUInt16(header, 26) + BitConverter.ToUInt16(header, 28);
                byte[] raw = Get(url, start, start + e[1] - 1);
                if (e[0] == 0) return raw;
                if (e[0] != 8) throw new InvalidDataException(suffix + ": unsupported compression.");
                using (var input = new DeflateStream(new MemoryStream(raw), CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    input.CopyTo(output);
                    return output.ToArray();
                }
            }

            static long Length(string url)
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.UserAgent = "ryGT7-launcher";
                request.AddRange(0, 0);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    string range = response.Headers["Content-Range"];
                    long total;
                    if (range == null || !long.TryParse(range.Substring(range.LastIndexOf('/') + 1), out total))
                        throw new InvalidDataException("Could not read the size of Streamline's zip.");
                    return total;
                }
            }

            static int LastIndex(byte[] data, byte[] pattern)
            {
                for (int i = data.Length - pattern.Length; i >= 0; --i)
                {
                    int j = 0;
                    while (j < pattern.Length && data[i + j] == pattern[j]) ++j;
                    if (j == pattern.Length) return i;
                }
                return -1;
            }
        }

        static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) text.Append(b.ToString("x2"));
            return text.ToString();
        }

        // Authenticode: the file's signature verifies (WinVerifyTrust) and its signer is NVIDIA.
        static void CheckNvidiaSignature(string path, string name)
        {
            var file = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = path
            };
            IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(file));
            try
            {
                Marshal.StructureToPtr(file, filePointer, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = 2,            // WTD_UI_NONE
                    fdwRevocationChecks = 0,   // WTD_REVOKE_NONE
                    dwUnionChoice = 1,         // WTD_CHOICE_FILE
                    pFile = filePointer,
                    dwStateAction = 0,
                    dwProvFlags = 0x00000080   // WTD_REVOCATION_CHECK_NONE
                };
                Guid action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
                int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                if (result != 0)
                    throw new InvalidDataException(name + ": its digital signature does not verify (0x" + result.ToString("x8") + ").");
            }
            finally
            {
                Marshal.FreeHGlobal(filePointer);
            }
            var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            if (signer.GetNameInfo(X509NameType.SimpleName, false) != "NVIDIA Corporation")
                throw new InvalidDataException(name + ": not signed by NVIDIA Corporation.");
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        const string Notice =
            "NVIDIA files in this folder\r\n" +
            "===========================\r\n\r\n" +
            "Downloaded by RyGT7 from NVIDIA's own GitHub repositories on your request, checked against\r\n" +
            "pinned SHA-256 hashes and NVIDIA Corporation's digital signature. They are NVIDIA's\r\n" +
            "software, not part of RyGT7, and RyGT7 is not sponsored or endorsed by NVIDIA.\r\n\r\n" +
            "nvngx_dlss.dll, nvngx_dlssg.dll - NVIDIA DLSS " + DlssVersion + "\r\n" +
            "  https://github.com/NVIDIA/DLSS (lib/Windows_x86_64/rel)\r\n" +
            "  Licensed under the NVIDIA RTX SDKs License:\r\n" +
            "  https://github.com/NVIDIA/DLSS/blob/v310.9.16/LICENSE.txt\r\n\r\n" +
            "sl.interposer.dll, sl.common.dll, sl.dlss.dll, sl.dlss_g.dll, sl.reflex.dll,\r\n" +
            "sl.pcl.dll, NvLowLatencyVk.dll - NVIDIA Streamline " + StreamlineVersion + "\r\n" +
            "  https://github.com/NVIDIA-RTX/Streamline (release zip, bin/x64)\r\n" +
            "  Copyright (c) 2023 NVIDIA CORPORATION. All rights reserved. MIT license with NVIDIA's\r\n" +
            "  terms: https://github.com/NVIDIA-RTX/Streamline/blob/v2.14.16/license.txt\r\n" +
            "  Third-party notices: in Streamline's release (3rd-party-licenses.md).\r\n\r\n" +
            "NVIDIA, DLSS and Streamline are trademarks of NVIDIA Corporation.\r\n";
    }
}
