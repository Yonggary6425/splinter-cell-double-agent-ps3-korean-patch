// Splinter Cell Double Agent (PS3, BLUS30032) Korean patch - ISO patcher core
// C# 5 (compiles with Windows PowerShell 5.1 Add-Type and mono mcs)
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ScdaKor
{
    public class DirRec
    {
        public long RecPos;      // absolute byte offset of the directory record in the ISO
        public uint Extent;      // LBA
        public uint Size;        // bytes
        public bool IsDir;
        public string Name;
    }

    public class Iso : IDisposable
    {
        const int SS = 2048;
        FileStream fs;
        public List<uint> RootExtents = new List<uint>();
        public List<uint> RootSizes = new List<uint>();
        public List<bool> RootJoliet = new List<bool>();
        public List<long> VdPos = new List<long>();
        public uint VolumeSectors;
        public uint EndSector;          // next free sector at end of image
        public bool HadUdf;
        public Action<string> Log = delegate { };

        public Iso(string path, bool write)
        {
            fs = new FileStream(path, FileMode.Open, write ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read, 1 << 20);
            for (int s = 16; s < 64; s++)
            {
                byte[] b = ReadSectors((uint)s, 1);
                string id = Encoding.ASCII.GetString(b, 1, 5);
                if (id == "CD001")
                {
                    if (b[0] == 1 || b[0] == 2)
                    {
                        VdPos.Add((long)s * SS);
                        RootExtents.Add(BitConverter.ToUInt32(b, 156 + 2));
                        RootSizes.Add(BitConverter.ToUInt32(b, 156 + 10));
                        RootJoliet.Add(b[0] == 2 && b[88] == 0x25 && b[89] == 0x2F);
                        if (b[0] == 1) VolumeSectors = BitConverter.ToUInt32(b, 80);
                    }
                    if (b[0] == 255) continue;
                }
                else if (id == "BEA01" || id == "NSR02" || id == "NSR03" || id == "TEA01")
                {
                    HadUdf = true;
                }
            }
            if (VdPos.Count == 0) throw new Exception("ISO9660 볼륨 정보를 찾을 수 없습니다. PS3 게임 ISO가 맞는지 확인하세요.");
            long len = fs.Length;
            EndSector = (uint)((len + SS - 1) / SS);
            if (VolumeSectors > EndSector) EndSector = VolumeSectors;
        }

        public void Dispose() { if (fs != null) { fs.Dispose(); fs = null; } }

        public byte[] ReadSectors(uint lba, int count)
        {
            byte[] b = new byte[count * SS];
            fs.Position = (long)lba * SS;
            int got = 0;
            while (got < b.Length) { int r = fs.Read(b, got, b.Length - got); if (r <= 0) break; got += r; }
            return b;
        }

        public byte[] ReadExtent(uint lba, uint size)
        {
            byte[] b = new byte[size];
            fs.Position = (long)lba * SS;
            int got = 0;
            while (got < b.Length) { int r = fs.Read(b, got, b.Length - got); if (r <= 0) throw new Exception("ISO 읽기 오류"); got += r; }
            return b;
        }

        List<DirRec> ListDir(uint extent, uint size) { return ListDir(extent, size, false); }
        List<DirRec> ListDir(uint extent, uint size, bool joliet)
        {
            List<DirRec> res = new List<DirRec>();
            byte[] d = ReadExtent(extent, (uint)(((size + SS - 1) / SS) * SS));
            int p = 0;
            while (p < size)
            {
                int len = d[p];
                if (len == 0) { p = ((p / SS) + 1) * SS; continue; }
                int nl = d[p + 32];
                string name;
                if (nl == 1 && (d[p + 33] == 0 || d[p + 33] == 1)) name = d[p + 33] == 0 ? "." : "..";
                else name = joliet ? Encoding.BigEndianUnicode.GetString(d, p + 33, nl) : Encoding.ASCII.GetString(d, p + 33, nl);
                int semi = name.IndexOf(';'); if (semi >= 0) name = name.Substring(0, semi);
                if (name.EndsWith(".") && name.Length > 1) name = name.TrimEnd('.');
                DirRec r = new DirRec();
                r.RecPos = (long)extent * SS + p;
                r.Extent = BitConverter.ToUInt32(d, p + 2);
                r.Size = BitConverter.ToUInt32(d, p + 10);
                r.IsDir = (d[p + 25] & 2) != 0;
                r.Name = name;
                res.Add(r);
                p += len;
            }
            return res;
        }

        // all records (from every volume descriptor tree) that point to the given path
        public List<DirRec> Find(string path)
        {
            string[] parts = path.Replace('\\', '/').Trim('/').Split('/');
            List<DirRec> found = new List<DirRec>();
            for (int v = 0; v < RootExtents.Count; v++)
            {
                uint ext = RootExtents[v], sz = RootSizes[v];
                DirRec cur = null;
                bool ok = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    DirRec hit = null;
                    foreach (DirRec r in ListDir(ext, sz, RootJoliet[v]))
                        if (string.Equals(r.Name, parts[i], StringComparison.OrdinalIgnoreCase)) { hit = r; break; }
                    if (hit == null) { ok = false; break; }
                    cur = hit; ext = hit.Extent; sz = hit.Size;
                }
                if (ok && cur != null && !cur.IsDir) found.Add(cur);
            }
            return found;
        }

        public List<string> ListFiles(string dirPath)
        {
            string[] parts = dirPath.Replace('\\', '/').Trim('/').Split('/');
            uint ext = RootExtents[0], sz = RootSizes[0];
            foreach (string part in parts)
            {
                DirRec hit = null;
                foreach (DirRec r in ListDir(ext, sz))
                    if (r.IsDir && string.Equals(r.Name, part, StringComparison.OrdinalIgnoreCase)) { hit = r; break; }
                if (hit == null) return new List<string>();
                ext = hit.Extent; sz = hit.Size;
            }
            List<string> res = new List<string>();
            foreach (DirRec r in ListDir(ext, sz)) if (!r.IsDir) res.Add(r.Name);
            return res;
        }

        public byte[] ReadFile(string path)
        {
            List<DirRec> l = Find(path);
            if (l.Count == 0) throw new FileNotFoundException("ISO 안에 파일이 없습니다: " + path);
            return ReadExtent(l[0].Extent, l[0].Size);
        }

        static void PutBoth32(byte[] b, int off, uint v)
        {
            byte[] le = BitConverter.GetBytes(v);
            Array.Copy(le, 0, b, off, 4);
            b[off + 4] = le[3]; b[off + 5] = le[2]; b[off + 6] = le[1]; b[off + 7] = le[0];
        }

        public void ReplaceFile(string path, byte[] data)
        {
            List<DirRec> recs = Find(path);
            if (recs.Count == 0) throw new FileNotFoundException("ISO 안에 파일이 없습니다: " + path);
            DirRec r0 = recs[0];
            uint oldSec = (r0.Size + SS - 1) / SS;
            uint newSec = ((uint)data.Length + SS - 1) / SS;
            uint lba = r0.Extent;
            if (newSec > oldSec) { lba = EndSector; EndSector += newSec; }
            fs.Position = (long)lba * SS;
            fs.Write(data, 0, data.Length);
            int pad = (int)(newSec * SS - data.Length);
            if (pad > 0) fs.Write(new byte[pad], 0, pad);
            if (newSec < oldSec && lba == r0.Extent)
            {
                byte[] z = new byte[SS];
                for (uint s = newSec; s < oldSec; s++) fs.Write(z, 0, SS);
            }
            foreach (DirRec r in recs)
            {
                byte[] rec = new byte[18];
                fs.Position = r.RecPos; fs.Read(rec, 0, 18);
                PutBoth32(rec, 2, lba);
                PutBoth32(rec, 10, (uint)data.Length);
                fs.Position = r.RecPos; fs.Write(rec, 0, 18);
            }
        }

        public void Finish()
        {
            // volume size in every volume descriptor
            foreach (long vp in VdPos)
            {
                byte[] b = new byte[8];
                PutBoth32(b, 0, EndSector);
                fs.Position = vp + 80; fs.Write(b, 0, 8);
            }
            long want = (long)EndSector * SS;
            if (fs.Length < want) fs.SetLength(want);
            // PS3 disc region table (sector 0): extend the last plain region to the new end
            byte[] s0 = ReadSectors(0, 1);
            uint n = BE32(s0, 0);
            if (n >= 1 && n <= 32)
            {
                int last = 8 + (int)(n - 1) * 8;
                uint st = BE32(s0, last), en = BE32(s0, last + 4);
                if (en + 1 >= VolumeSectors - 1 && en < EndSector - 1 && st <= en)
                {
                    uint ne = EndSector - 1;
                    s0[last + 4] = (byte)(ne >> 24); s0[last + 5] = (byte)(ne >> 16); s0[last + 6] = (byte)(ne >> 8); s0[last + 7] = (byte)ne;
                    fs.Position = 0; fs.Write(s0, 0, SS);
                    Log("  디스크 영역 표 갱신: 마지막 영역 끝 " + en + " -> " + ne);
                }
            }
            fs.Flush();
        }

        // neutralize UDF descriptors so that readers use the (patched) ISO9660 tree
        public int DisableUdf()
        {
            int n = 0;
            for (int s = 16; s < 64; s++)
            {
                byte[] b = ReadSectors((uint)s, 1);
                string id = Encoding.ASCII.GetString(b, 1, 5);
                if (id == "NSR02" || id == "NSR03")
                {
                    b[1] = (byte)'X';
                    fs.Position = (long)s * SS; fs.Write(b, 0, SS); n++;
                }
            }
            // anchor volume descriptor pointers: readers locate UDF through these
            uint[] cand = new uint[] { 256, VolumeSectors - 257, VolumeSectors - 1, 512 };
            foreach (uint s in cand)
            {
                if (s < 64 || s >= VolumeSectors) continue;
                byte[] b = ReadSectors(s, 1);
                if (b[0] == 2 && b[1] == 0)
                {
                    fs.Position = (long)s * SS; fs.Write(new byte[SS], 0, SS); n++;
                }
            }
            return n;
        }

        public bool HasUdfAnchor()
        {
            byte[] b = ReadSectors(256, 1);
            return b[0] == 2 && b[1] == 0;
        }

        static uint BE32(byte[] b, int o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }
    }

    public static class Patcher
    {
        public const string ElfSha256 = "04B547DA62BBF8DFFE43A34E72606C505F26F128EEA038E25840BC6A79277211";
        public static Action<string> Log = Console.WriteLine;

        static uint U32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }

        public static byte[] Gunzip(string path)
        {
            using (FileStream f = File.OpenRead(path))
            using (GZipStream g = new GZipStream(f, CompressionMode.Decompress))
            using (MemoryStream m = new MemoryStream()) { g.CopyTo(m); return m.ToArray(); }
        }

        public static string Sha256(byte[] b)
        {
            using (SHA256 s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "");
        }

        // returns null if the original does not match (size check)
        public static byte[] ApplyKpt(byte[] orig, byte[] kpt)
        {
            if (Encoding.ASCII.GetString(kpt, 0, 4) != "KPT1") throw new Exception("잘못된 패치 파일");
            uint osize = U32(kpt, 4); int n = (int)U32(kpt, 8);
            if (orig.Length != osize) return null;
            byte[] src;
            using (MemoryStream mi = new MemoryStream(orig, 4, orig.Length - 4))
            using (DeflateStream ds = new DeflateStream(mi, CompressionMode.Decompress))
            using (MemoryStream mo = new MemoryStream()) { ds.CopyTo(mo); src = mo.ToArray(); }
            MemoryStream outp = new MemoryStream(src.Length + 4000000);
            int pos = 12, last = 0;
            for (int i = 0; i < n; i++)
            {
                int s = (int)U32(kpt, pos), e = (int)U32(kpt, pos + 4), l = (int)U32(kpt, pos + 8); pos += 12;
                outp.Write(src, last, s - last);
                outp.Write(kpt, pos, l); pos += l;
                last = e;
            }
            outp.Write(src, last, src.Length - last);
            src = null;
            int len = (int)outp.Length;
            MemoryStream res = new MemoryStream(len / 2);
            res.WriteByte((byte)(len >> 24)); res.WriteByte((byte)(len >> 16)); res.WriteByte((byte)(len >> 8)); res.WriteByte((byte)len);
            using (DeflateStream dz = new DeflateStream(res, CompressionLevel.Optimal, true)) dz.Write(outp.GetBuffer(), 0, len);
            outp.Dispose();
            return res.ToArray();
        }

        public static byte[] ApplyKpe(byte[] elf, byte[] kpe)
        {
            if (Encoding.ASCII.GetString(kpe, 0, 4) != "KPE1") throw new Exception("잘못된 패치 파일");
            int osize = (int)U32(kpe, 4), n = (int)U32(kpe, 8), hl = (int)U32(kpe, 12);
            if (elf.Length != osize) throw new Exception("원본 ELF 크기가 다릅니다");
            byte[] src = (byte[])elf.Clone();
            int pos = 16 + hl;
            for (int i = 0; i < n; i++)
            {
                int s = (int)U32(kpe, pos), l = (int)U32(kpe, pos + 8); pos += 12;
                Array.Copy(kpe, pos, src, s, l); pos += l;
            }
            byte[] outp = new byte[hl + src.Length];
            Array.Copy(kpe, 16, outp, 0, hl);
            Array.Copy(src, 0, outp, hl, src.Length);
            return outp;
        }

        // patchDir: folder holding data\ and files\ ; elfPath may be null
        public static int PatchIso(string isoPath, string patchDir, string elfPath)
        {
            int changed = 0, skipped = 0;
            using (Iso iso = new Iso(isoPath, true))
            {
                iso.Log = Log;
                const string U = "PS3_GAME/USRDIR/";
                if (iso.Find(U + "offline/system/menu_int.lin").Count == 0)
                    throw new Exception("스플린터 셀 더블 에이전트(BLUS30032) ISO가 아니거나 암호화된 ISO입니다.");
                if (iso.HadUdf || iso.HasUdfAnchor())
                {
                    int u = iso.DisableUdf();
                    if (u > 0) Log("  UDF 정보를 비활성화했습니다 (ISO9660 정보만 사용).");
                }
                string data = Path.Combine(patchDir, "data");
                // .lin patches
                foreach (string k in Directory.GetFiles(data, "*.kpt", SearchOption.AllDirectories))
                {
                    string rel = k.Substring(data.Length + 1).Replace('\\', '/');
                    rel = rel.Substring(0, rel.Length - 4);
                    byte[] orig;
                    try { orig = iso.ReadFile(U + rel); } catch (FileNotFoundException) { Log("  없음, 건너뜀: " + rel); skipped++; continue; }
                    byte[] np = ApplyKpt(orig, Gunzip(k));
                    if (np == null) { Log("  원본과 달라 건너뜀(이미 패치됨?): " + rel); skipped++; continue; }
                    iso.ReplaceFile(U + rel, np);
                    Log("패치: " + rel);
                    changed++;
                    np = null; orig = null; GC.Collect();
                }
                // executable
                foreach (string k in Directory.GetFiles(data, "*.kpe", SearchOption.AllDirectories))
                {
                    string rel = k.Substring(data.Length + 1).Replace('\\', '/');
                    rel = rel.Substring(0, rel.Length - 4);
                    if (elfPath == null || !File.Exists(elfPath))
                    {
                        Log("  [실행 파일] 복호화된 sc4_offline.elf 가 없어 실행 파일 패치를 건너뜁니다.");
                        skipped++; continue;
                    }
                    byte[] elf = File.ReadAllBytes(elfPath);
                    if (Sha256(elf) != ElfSha256) { Log("  [실행 파일] sc4_offline.elf 가 원본과 달라 건너뜁니다."); skipped++; continue; }
                    iso.ReplaceFile(U + rel, ApplyKpe(elf, File.ReadAllBytes(k)));
                    Log("패치: " + rel);
                    changed++;
                }
                // plain files
                string files = Path.Combine(patchDir, "files");
                if (Directory.Exists(files))
                    foreach (string f in Directory.GetFiles(files, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(files.Length + 1).Replace('\\', '/');
                        if (iso.Find(U + rel).Count == 0) { Log("  없음, 건너뜀: " + rel); skipped++; continue; }
                        iso.ReplaceFile(U + rel, File.ReadAllBytes(f));
                        changed++;
                    }
                Log("텍스트 파일 교체 완료");
                iso.Finish();
            }
            Log("바뀐 파일 " + changed + "개, 건너뜀 " + skipped + "개");
            return skipped;
        }

        public static void ExtractFile(string isoPath, string inner, string outPath)
        {
            using (Iso iso = new Iso(isoPath, false)) File.WriteAllBytes(outPath, iso.ReadFile(inner));
        }

        public static bool IsoHasFile(string isoPath, string inner)
        {
            using (Iso iso = new Iso(isoPath, false)) return iso.Find(inner).Count > 0;
        }
    }
}
