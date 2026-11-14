// SdFake - acesso ao Windows: arquivos sem cache, disco bruto, lista de unidades.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SdFake
{
    static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        public const uint CREATE_ALWAYS = 2, OPEN_EXISTING = 3;
        public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        public const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
        public const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;

        public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;
        public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        public const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
        public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY = 0x00070000;
        public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
        public const uint FSCTL_LOCK_VOLUME = 0x00090018;
        public const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
        public const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, int bytes, out int read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int bytes, out int written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetFilePointerEx(SafeFileHandle h, long distance, out long newPosition, uint method);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FlushFileBuffers(SafeFileHandle h);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuffer, int inSize,
            IntPtr outBuffer, int outSize, out int returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool GetDiskFreeSpaceExW(string directory, out ulong freeForCaller, out ulong total, out ulong totalFree);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindFirstVolumeW(StringBuilder name, int length);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool FindNextVolumeW(IntPtr find, StringBuilder name, int length);

        [DllImport("kernel32.dll")]
        public static extern bool FindVolumeClose(IntPtr find);

        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint flags);

        [DllImport("kernel32.dll")]
        public static extern uint SetErrorMode(uint mode);

        public static TestIOException Error(string what)
        {
            int code = Marshal.GetLastWin32Error();
            return Error(what, code);
        }

        public static TestIOException Error(string what, int code)
        {
            string msg;
            try { msg = new Win32Exception(code).Message; } catch (Exception) { msg = "erro"; }
            return new TestIOException(what + ": " + msg.Trim() + " (código " + code + ")", code);
        }

        /// <summary>Impede o PC de dormir durante o teste.</summary>
        public static void KeepAwake(bool on)
        {
            try { SetThreadExecutionState(on ? 0x80000001u : 0x80000000u); } catch (Exception) { }
        }
    }

    /// <summary>Arquivo aberto sem cache do Windows: o que e lido vem mesmo do cartao.</summary>
    sealed class WinFile : ITestFile
    {
        SafeFileHandle h;
        readonly string path;

        WinFile(SafeFileHandle handle, string path) { h = handle; this.path = path; }

        public static WinFile Create(string path)
        {
            SafeFileHandle h = Native.CreateFileW(path, Native.GENERIC_WRITE, Native.FILE_SHARE_READ, IntPtr.Zero,
                Native.CREATE_ALWAYS, Native.FILE_FLAG_NO_BUFFERING | Native.FILE_FLAG_SEQUENTIAL_SCAN, IntPtr.Zero);
            if (h.IsInvalid) throw Native.Error("Não foi possível criar o arquivo de teste");
            return new WinFile(h, path);
        }

        public static WinFile OpenRead(string path)
        {
            SafeFileHandle h = Native.CreateFileW(path, Native.GENERIC_READ, Native.FILE_SHARE_READ, IntPtr.Zero,
                Native.OPEN_EXISTING, Native.FILE_FLAG_NO_BUFFERING | Native.FILE_FLAG_SEQUENTIAL_SCAN, IntPtr.Zero);
            if (h.IsInvalid) throw Native.Error("Não foi possível abrir o arquivo de teste");
            return new WinFile(h, path);
        }

        public void Write(IntPtr buffer, int bytes)
        {
            int written;
            if (!Native.WriteFile(h, buffer, bytes, out written, IntPtr.Zero)) throw Native.Error("Falha ao gravar");
            if (written != bytes) throw Native.Error("Gravação incompleta", 112);
        }

        public void Read(long offset, IntPtr buffer, int bytes)
        {
            long np;
            if (!Native.SetFilePointerEx(h, offset, out np, 0)) throw Native.Error("Falha ao posicionar a leitura");
            int read;
            if (!Native.ReadFile(h, buffer, bytes, out read, IntPtr.Zero)) throw Native.Error("Falha ao ler");
            if (read != bytes) throw Native.Error("Leitura incompleta", 38);
        }

        public void Flush()
        {
            if (!Native.FlushFileBuffers(h)) throw Native.Error("Falha ao finalizar a gravação");
        }

        public void Dispose()
        {
            if (h != null) { h.Dispose(); h = null; }
        }
    }

    /// <summary>Pasta de teste dentro do dispositivo (raiz\SdFake_Teste).</summary>
    public sealed class WinStore : ITestStore
    {
        public const string FolderName = "SdFake_Teste";
        readonly string root;
        readonly string dir;

        public WinStore(string root)
        {
            this.root = root.EndsWith("\\") ? root : root + "\\";
            dir = Path.Combine(this.root, FolderName);
        }

        public void Prepare()
        {
            try
            {
                RemoveOurFiles();
                Directory.CreateDirectory(dir);
            }
            catch (UnauthorizedAccessException)
            {
                throw new TestAbortException("Sem permissão para gravar no dispositivo. Verifique se ele não está protegido contra gravação (chavinha LOCK).");
            }
            catch (IOException e)
            {
                throw new TestAbortException("Não foi possível preparar a pasta de teste no dispositivo: " + e.Message);
            }
        }

        public long FreeBytes()
        {
            ulong free, total, totalFree;
            if (!Native.GetDiskFreeSpaceExW(root, out free, out total, out totalFree)) throw Native.Error("Não foi possível ler o espaço livre");
            return (long)free;
        }

        public long TotalBytes()
        {
            ulong free, total, totalFree;
            if (!Native.GetDiskFreeSpaceExW(root, out free, out total, out totalFree)) throw Native.Error("Não foi possível ler o tamanho do dispositivo");
            return (long)total;
        }

        public ITestFile Create(string name) { return WinFile.Create(Path.Combine(dir, name)); }
        public ITestFile Open(string name) { return WinFile.OpenRead(Path.Combine(dir, name)); }

        public void Cleanup()
        {
            try { RemoveOurFiles(); } catch (Exception) { }
        }

        /// <summary>Apaga somente os arquivos SDFnnnnn.bin criados por este programa, e a pasta se ficar vazia.</summary>
        void RemoveOurFiles()
        {
            if (!Directory.Exists(dir)) return;
            foreach (string f in Directory.GetFiles(dir, "SDF*.bin"))
            {
                string n = Path.GetFileName(f);
                if (n.Length != 12) continue;
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch (Exception) { }
            }
            try
            {
                if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
            }
            catch (Exception) { }
        }
    }

    public class DriveEntry
    {
        public string Letter;       // "E:"
        public string Root;         // "E:\"
        public string Label = "";
        public string FileSystem = "";
        public string Model = "";
        public long Total, Free;
        public long MediaBytes = -1;
        /// <summary>Tamanho que o dispositivo fisico anuncia (-1 = desconhecido). Pode ser maior que o volume formatado.</summary>
        public long DeviceBytes = -1;

        /// <summary>true quando o volume formatado e bem menor do que o dispositivo anuncia.</summary>
        public bool IsPartial { get { return Ready && DeviceBytes > 0 && Total < DeviceBytes * 0.9; } }
        public bool Ready;
        public bool Removable;
        public bool IsFolder;
        public int Bus;

        public bool HasMedia { get { return Ready || MediaBytes > 0; } }

        public override string ToString()
        {
            string name = Label.Length > 0 ? Label : (Model.Length > 0 ? Model : (Removable ? "Disco removível" : "Disco USB"));
            if (Ready && IsPartial)
                return Letter + "  " + name + "  —  anuncia " + Fmt.Bytes(DeviceBytes) + ", formatado com " + Fmt.Bytes(Total);
            if (Ready) return Letter + "  " + name + "  —  " + Fmt.Bytes(Total) + (Removable ? "" : "  (disco USB externo)");
            if (MediaBytes > 0) return Letter + "  " + name + "  —  " + Fmt.Bytes(MediaBytes) + " (sem formatação reconhecida)";
            return Letter + "  " + name + "  —  vazio (insira o cartão)";
        }
    }

    public static class DriveCatalog
    {
        class BusInfo { public int Bus; public bool Removable; public string Model = ""; }

        public static string SystemLetter()
        {
            try { return Path.GetPathRoot(Environment.SystemDirectory).Substring(0, 2).ToUpperInvariant(); }
            catch (Exception) { return "C:"; }
        }

        /// <summary>Uma pasta comum como alvo do Teste Completo (opcao de diagnostico --pasta).</summary>
        public static DriveEntry Folder(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (!Directory.Exists(full)) return null;
                ulong free, total, totalFree;
                if (!Native.GetDiskFreeSpaceExW(full, out free, out total, out totalFree)) return null;
                DriveEntry e = new DriveEntry();
                e.Letter = "Pasta";
                e.Root = full.EndsWith("\\") ? full : full + "\\";
                e.Label = full;
                e.FileSystem = "pasta";
                e.Total = (long)total;
                e.Free = (long)free;
                e.Ready = true;
                e.Removable = true;
                e.IsFolder = true;
                return e;
            }
            catch (Exception) { return null; }
        }

        public static bool IsPortableBus(int bus) { return bus == 7 || bus == 12 || bus == 13; }   // USB, SD, MMC

        public static List<DriveEntry> List()
        {
            List<DriveEntry> result = new List<DriveEntry>();
            string sys = SystemLetter();
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); } catch (Exception) { return result; }
            foreach (DriveInfo di in drives)
            {
                try
                {
                    if (di.DriveType != DriveType.Removable && di.DriveType != DriveType.Fixed) continue;
                    string letter = di.Name.Substring(0, 2).ToUpperInvariant();
                    if (letter == sys) continue;
                    BusInfo bi = QueryBus(@"\\.\" + letter);
                    bool portable = bi != null && IsPortableBus(bi.Bus);
                    if (di.DriveType == DriveType.Fixed && !portable) continue;

                    DriveEntry e = new DriveEntry();
                    e.Letter = letter;
                    e.Root = letter + "\\";
                    e.Removable = di.DriveType == DriveType.Removable;
                    if (bi != null) { e.Bus = bi.Bus; e.Model = bi.Model; }
                    try
                    {
                        if (di.IsReady)
                        {
                            e.Ready = true;
                            e.Label = di.VolumeLabel ?? "";
                            e.FileSystem = di.DriveFormat ?? "";
                            e.Total = di.TotalSize;
                            e.Free = di.AvailableFreeSpace;
                        }
                    }
                    catch (Exception) { e.Ready = false; }
                    if (!e.Ready) e.MediaBytes = MediaLength(@"\\.\" + letter);
                    if (e.HasMedia) e.DeviceBytes = DeviceLengthOf(letter);
                    result.Add(e);
                }
                catch (Exception) { }
            }
            result.Sort(delegate(DriveEntry x, DriveEntry y)
            {
                if (x.Removable != y.Removable) return x.Removable ? -1 : 1;
                return string.CompareOrdinal(x.Letter, y.Letter);
            });
            return result;
        }

        /// <summary>Numero do disco fisico por tras de um volume, ou -1.</summary>
        public static int DeviceNumberOf(string device)
        {
            using (SafeFileHandle h = Native.CreateFileW(device, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return -1;
                IntPtr buf = Marshal.AllocHGlobal(12);
                try
                {
                    int ret;
                    if (!Native.DeviceIoControl(h, Native.IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, buf, 12, out ret, IntPtr.Zero)) return -1;
                    if (Marshal.ReadInt32(buf, 0) != 7) return -1;      // FILE_DEVICE_DISK
                    return Marshal.ReadInt32(buf, 4);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        /// <summary>
        /// Tamanho que o disco fisico por tras de uma letra anuncia, ou -1. Nao precisa de administrador.
        /// E diferente do tamanho do volume: um cartao pode anunciar 128 GB e estar formatado com 243 MB.
        /// </summary>
        public static long DeviceLengthOf(string letter)
        {
            if (letter == null || letter.Length < 2 || letter[1] != ':') return -1;
            int number = DeviceNumberOf(@"\\.\" + letter.Substring(0, 2));
            if (number < 0) return -1;
            using (SafeFileHandle h = Native.CreateFileW(@"\\.\PhysicalDrive" + number, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return -1;
                IntPtr buf = Marshal.AllocHGlobal(256);
                try
                {
                    int ret;
                    if (!Native.DeviceIoControl(h, Native.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, IntPtr.Zero, 0, buf, 256, out ret, IntPtr.Zero)) return -1;
                    if (ret < 32) return -1;
                    return Marshal.ReadInt64(buf, 24);   // DISK_GEOMETRY_EX.DiskSize
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        static long MediaLength(string device)
        {
            using (SafeFileHandle h = Native.CreateFileW(device, Native.GENERIC_READ, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return -1;
                IntPtr buf = Marshal.AllocHGlobal(8);
                try
                {
                    int ret;
                    if (!Native.DeviceIoControl(h, Native.IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, buf, 8, out ret, IntPtr.Zero)) return -1;
                    return Marshal.ReadInt64(buf, 0);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        static BusInfo QueryBus(string device)
        {
            using (SafeFileHandle h = Native.CreateFileW(device, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                const int outSize = 1024;
                IntPtr query = Marshal.AllocHGlobal(12);
                IntPtr buf = Marshal.AllocHGlobal(outSize);
                try
                {
                    Marshal.WriteInt32(query, 0, 0);   // StorageDeviceProperty
                    Marshal.WriteInt32(query, 4, 0);   // PropertyStandardQuery
                    Marshal.WriteInt32(query, 8, 0);
                    int ret;
                    if (!Native.DeviceIoControl(h, Native.IOCTL_STORAGE_QUERY_PROPERTY, query, 12, buf, outSize, out ret, IntPtr.Zero)) return null;
                    if (ret < 36) return null;
                    BusInfo bi = new BusInfo();
                    bi.Removable = Marshal.ReadByte(buf, 10) != 0;
                    bi.Bus = Marshal.ReadInt32(buf, 28);
                    string vendor = ReadAnsi(buf, Marshal.ReadInt32(buf, 12), ret);
                    string product = ReadAnsi(buf, Marshal.ReadInt32(buf, 16), ret);
                    bi.Model = (vendor + " " + product).Trim();
                    return bi;
                }
                finally
                {
                    Marshal.FreeHGlobal(query);
                    Marshal.FreeHGlobal(buf);
                }
            }
        }

        static string ReadAnsi(IntPtr buf, int offset, int limit)
        {
            if (offset <= 0 || offset >= limit) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = offset; i < limit; i++)
            {
                byte c = Marshal.ReadByte(buf, i);
                if (c == 0) break;
                sb.Append(c >= 32 && c < 127 ? (char)c : ' ');
            }
            return sb.ToString().Trim();
        }

        /// <summary>true se a letra aponta para cartao de memoria / dispositivo USB (nunca o disco do sistema).</summary>
        public static bool IsSafeTarget(string letter, out string reason)
        {
            reason = null;
            letter = letter.Substring(0, 2).ToUpperInvariant();
            if (letter == SystemLetter()) { reason = "Esta é a unidade do sistema."; return false; }
            DriveType type;
            try { type = new DriveInfo(letter + "\\").DriveType; }
            catch (Exception) { reason = "Unidade inválida."; return false; }
            BusInfo bi = QueryBus(@"\\.\" + letter);
            bool portable = bi != null && IsPortableBus(bi.Bus);
            if (type != DriveType.Removable && !(type == DriveType.Fixed && portable))
            {
                reason = "O teste rápido só é permitido em cartões de memória, pendrives e discos USB.";
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Disco fisico aberto para leitura/gravacao direta, com os volumes travados e
    /// desmontados durante o teste (o Windows exige isso para gravar por baixo do sistema de arquivos).
    /// </summary>
    public sealed class RawDisk : IBlockDevice, IDisposable
    {
        SafeFileHandle disk;
        readonly List<SafeFileHandle> volumes = new List<SafeFileHandle>();
        long size;
        int sector = 512;

        public long Size { get { return size; } }
        public int SectorSize { get { return sector; } }

        RawDisk() { }

        public static RawDisk Open(string letter)
        {
            letter = letter.Substring(0, 2).ToUpperInvariant();
            string reason;
            if (!DriveCatalog.IsSafeTarget(letter, out reason)) throw new TestAbortException(reason);

            int number = DriveCatalog.DeviceNumberOf(@"\\.\" + letter);
            if (number < 0)
                throw new TestAbortException("Não foi possível identificar o dispositivo em " + letter + ". Verifique se o cartão está inserido.");

            // Defesa extra: jamais tocar no disco do Windows nem no disco de onde este programa esta rodando.
            int sysDisk = DriveCatalog.DeviceNumberOf(@"\\.\" + DriveCatalog.SystemLetter());
            if (number == sysDisk) throw new TestAbortException("Este dispositivo é o disco do sistema. Teste cancelado por segurança.");
            try
            {
                string exeRoot = Path.GetPathRoot(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (exeRoot != null && exeRoot.Length >= 2 && exeRoot[1] == ':')
                {
                    int exeDisk = DriveCatalog.DeviceNumberOf(@"\\.\" + exeRoot.Substring(0, 2));
                    if (number == exeDisk) throw new TestAbortException("O SdFake está rodando a partir deste mesmo dispositivo. Copie o programa para o PC antes de testar.");
                }
            }
            catch (TestAbortException) { throw; }
            catch (Exception) { }

            RawDisk d = new RawDisk();
            try
            {
                d.LockVolumes(number);
                d.disk = Native.CreateFileW(@"\\.\PhysicalDrive" + number, Native.GENERIC_READ | Native.GENERIC_WRITE,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
                    Native.FILE_FLAG_NO_BUFFERING | Native.FILE_FLAG_WRITE_THROUGH, IntPtr.Zero);
                if (d.disk.IsInvalid)
                {
                    int code = Marshal.GetLastWin32Error();
                    if (code == 5) throw NeedAdmin();
                    throw new TestAbortException(Native.Error("Não foi possível abrir o dispositivo", code).Message);
                }

                IntPtr buf = Marshal.AllocHGlobal(32);
                try
                {
                    int ret;
                    if (!Native.DeviceIoControl(d.disk, Native.IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, buf, 8, out ret, IntPtr.Zero))
                        throw new TestAbortException(Native.Error("Não foi possível ler o tamanho do dispositivo").Message);
                    d.size = Marshal.ReadInt64(buf, 0);
                    if (Native.DeviceIoControl(d.disk, Native.IOCTL_DISK_GET_DRIVE_GEOMETRY, IntPtr.Zero, 0, buf, 24, out ret, IntPtr.Zero))
                    {
                        int bps = Marshal.ReadInt32(buf, 20);
                        if (bps >= 512 && bps <= 65536) d.sector = bps;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
                if (d.size <= 0) throw new TestAbortException("O dispositivo informou tamanho zero. Verifique se o cartão está bem encaixado.");
                return d;
            }
            catch (Exception)
            {
                d.Dispose();
                throw;
            }
        }

        static TestAbortException NeedAdmin()
        {
            TestAbortException e = new TestAbortException("O Windows exige permissão de administrador para o teste rápido.");
            e.NeedAdmin = true;
            return e;
        }

        void LockVolumes(int diskNumber)
        {
            StringBuilder sb = new StringBuilder(300);
            IntPtr find = Native.FindFirstVolumeW(sb, sb.Capacity);
            if (find == new IntPtr(-1)) return;
            try
            {
                do
                {
                    string name = sb.ToString().TrimEnd('\\');
                    if (DriveCatalog.DeviceNumberOf(name) == diskNumber) LockOne(name);
                }
                while (Native.FindNextVolumeW(find, sb, sb.Capacity));
            }
            finally { Native.FindVolumeClose(find); }
        }

        void LockOne(string volume)
        {
            SafeFileHandle h = Native.CreateFileW(volume, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();
                if (code == 5) throw NeedAdmin();
                throw new TestAbortException(Native.Error("Não foi possível abrir o volume do dispositivo", code).Message);
            }
            volumes.Add(h);
            int ret;
            bool locked = false;
            for (int i = 0; i < 20 && !locked; i++)
            {
                locked = Native.DeviceIoControl(h, Native.FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out ret, IntPtr.Zero);
                if (!locked) Thread.Sleep(250);
            }
            if (!locked)
                throw new TestAbortException("O Windows não liberou o dispositivo porque algum programa está usando arquivos dele. "
                    + "Feche janelas do Explorador e programas abertos no cartão e tente de novo.");
            Native.DeviceIoControl(h, Native.FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out ret, IntPtr.Zero);
        }

        void Seek(long offset)
        {
            long np;
            if (!Native.SetFilePointerEx(disk, offset, out np, 0)) throw Native.Error("Falha ao posicionar no dispositivo");
        }

        public void Read(long offset, IntPtr buffer, int bytes)
        {
            Seek(offset);
            int read;
            if (!Native.ReadFile(disk, buffer, bytes, out read, IntPtr.Zero)) throw Native.Error("Falha ao ler do dispositivo");
            if (read != bytes) throw Native.Error("Leitura incompleta do dispositivo", 23);
        }

        public void Write(long offset, IntPtr buffer, int bytes)
        {
            Seek(offset);
            int written;
            if (!Native.WriteFile(disk, buffer, bytes, out written, IntPtr.Zero)) throw Native.Error("Falha ao gravar no dispositivo");
            if (written != bytes) throw Native.Error("Gravação incompleta no dispositivo", 29);
        }

        public void Dispose()
        {
            if (disk != null)
            {
                if (!disk.IsInvalid) { try { Native.FlushFileBuffers(disk); } catch (Exception) { } }
                disk.Dispose();
                disk = null;
            }
            foreach (SafeFileHandle h in volumes)
            {
                try
                {
                    int ret;
                    Native.DeviceIoControl(h, Native.FSCTL_UNLOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out ret, IntPtr.Zero);
                    h.Dispose();
                }
                catch (Exception) { }
            }
            volumes.Clear();
        }
    }
}
