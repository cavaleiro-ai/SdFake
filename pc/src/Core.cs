// SdFake - nucleo compartilhado: padrao de dados, interfaces, formatacao.
// Compila com o csc do .NET Framework 4 (C# 5).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SdFake
{
    /// <summary>Partial = a parte formatada esta boa, mas ela e bem menor do que o dispositivo anuncia.</summary>
    public enum Verdict { Genuine, Fake, Defective, Incomplete, Error, Partial }

    public class Progress
    {
        public string Phase;
        public double Fraction;
        public string Detail;
    }

    /// <summary>Erro de entrada/saida no dispositivo testado.</summary>
    public class TestIOException : Exception
    {
        public int Code;
        public bool DiskFull;
        public bool DeviceGone;
        public bool AccessDenied;
        public bool WriteProtected;
        public TestIOException(string message, int code) : base(message)
        {
            Code = code;
            DiskFull = code == 112 || code == 39;
            AccessDenied = code == 5;
            WriteProtected = code == 19;
            DeviceGone = code == 21 || code == 1167 || code == 55 || code == 433 || code == 15
                      || code == 2 || code == 3 || code == 1006 || code == 1110 || code == 1112;
        }
    }

    /// <summary>Erro que encerra o teste com uma mensagem para o usuario.</summary>
    public class TestAbortException : Exception
    {
        public bool NeedAdmin;
        public TestAbortException(string message) : base(message) { }
    }

    /// <summary>Um arquivo de teste (gravacao sequencial, leitura por posicao).</summary>
    public interface ITestFile : IDisposable
    {
        void Write(IntPtr buffer, int bytes);
        void Read(long offset, IntPtr buffer, int bytes);
        void Flush();
    }

    /// <summary>Onde os arquivos de teste sao gravados (pasta no cartao).</summary>
    public interface ITestStore
    {
        void Prepare();
        long FreeBytes();
        long TotalBytes();
        ITestFile Create(string name);
        ITestFile Open(string name);
        void Cleanup();
    }

    /// <summary>Acesso bruto por setores (teste rapido).</summary>
    public interface IBlockDevice
    {
        long Size { get; }
        int SectorSize { get; }
        void Read(long offset, IntPtr buffer, int bytes);
        void Write(long offset, IntPtr buffer, int bytes);
    }

    /// <summary>
    /// Padrao pseudoaleatorio. Cada bloco e unico e carrega no inicio a sua "chave"
    /// (posicao) e a semente da sessao, o que permite reconhecer dados descartados,
    /// corrompidos ou sobrescritos por outro bloco (cartoes que "dao a volta").
    /// </summary>
    public static unsafe class Pattern
    {
        public const int BlockSize = 1 << 20;   // 1 MiB
        public const int Piece = 4096;
        const ulong Golden = 0x9E3779B97F4A7C15UL;

        static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static void Fill(IntPtr dst, int bytes, ulong seed, ulong key)
        {
            ulong* p = (ulong*)dst;
            int n = bytes >> 3;
            ulong s = Mix(seed ^ Mix(key + Golden));
            p[0] = key;
            p[1] = seed;
            for (int i = 2; i < n; i++)
            {
                s += Golden;
                p[i] = Mix(s);
            }
        }

        public static bool Equal(IntPtr a, IntPtr b, int bytes)
        {
            ulong* x = (ulong*)a;
            ulong* y = (ulong*)b;
            int n = bytes >> 3;
            for (int i = 0; i < n; i++)
                if (x[i] != y[i]) return false;
            return true;
        }

        /// <summary>Quantos pedacos de 4 KiB diferem entre os dois buffers.</summary>
        public static int BadPieces(IntPtr a, IntPtr b, int bytes)
        {
            ulong* x = (ulong*)a;
            ulong* y = (ulong*)b;
            int perPiece = Piece >> 3;
            int pieces = bytes / Piece;
            int bad = 0;
            for (int p = 0; p < pieces; p++)
            {
                int o = p * perPiece;
                for (int i = 0; i < perPiece; i++)
                {
                    if (x[o + i] != y[o + i]) { bad++; break; }
                }
            }
            return bad;
        }

        /// <summary>Tudo 0x00 ou tudo 0xFF (dado nunca gravado de verdade).</summary>
        public static bool IsBlank(IntPtr a, int bytes)
        {
            ulong* x = (ulong*)a;
            int n = bytes >> 3;
            ulong first = x[0];
            if (first != 0UL && first != 0xFFFFFFFFFFFFFFFFUL) return false;
            for (int i = 1; i < n; i++)
                if (x[i] != first) return false;
            return true;
        }

        public static ulong StampKey(IntPtr a) { return ((ulong*)a)[0]; }
        public static ulong StampSeed(IntPtr a) { return ((ulong*)a)[1]; }

        public static ulong NewSeed()
        {
            byte[] b = Guid.NewGuid().ToByteArray();
            ulong s = BitConverter.ToUInt64(b, 0) ^ BitConverter.ToUInt64(b, 8) ^ (ulong)DateTime.UtcNow.Ticks;
            if (s == 0UL || s == 0xFFFFFFFFFFFFFFFFUL) s = 0x5DFA4E5DFA4E5DFAUL;
            return s;
        }
    }

    /// <summary>Buffer alinhado em pagina (exigido por E/S sem cache do Windows).</summary>
    public sealed class AlignedBuffer : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);

        public IntPtr Ptr;
        public readonly int Size;

        public AlignedBuffer(int size)
        {
            Size = size;
            Ptr = VirtualAlloc(IntPtr.Zero, (UIntPtr)(uint)size, 0x3000, 4);
            if (Ptr == IntPtr.Zero) throw new OutOfMemoryException("Sem memoria para o buffer de teste.");
        }

        public void Dispose()
        {
            if (Ptr != IntPtr.Zero)
            {
                VirtualFree(Ptr, UIntPtr.Zero, 0x8000);
                Ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>Velocidade media dos ultimos segundos.</summary>
    public class RateMeter
    {
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly Queue<KeyValuePair<double, long>> window = new Queue<KeyValuePair<double, long>>();
        long total;

        public void Add(long bytes)
        {
            total += bytes;
            double now = sw.Elapsed.TotalSeconds;
            window.Enqueue(new KeyValuePair<double, long>(now, total));
            while (window.Count > 2 && now - window.Peek().Key > 8.0) window.Dequeue();
        }

        public long Total { get { return total; } }

        /// <summary>MB/s (decimal) na janela recente.</summary>
        public double Recent
        {
            get
            {
                if (window.Count < 2) return Average;
                KeyValuePair<double, long> first = window.Peek();
                double dt = sw.Elapsed.TotalSeconds - first.Key;
                if (dt < 0.2) return Average;
                return (total - first.Value) / dt / 1e6;
            }
        }

        public double Average
        {
            get
            {
                double t = sw.Elapsed.TotalSeconds;
                return t < 0.05 ? 0 : total / t / 1e6;
            }
        }
    }

    public static class Fmt
    {
        static readonly CultureInfo Br = new CultureInfo("pt-BR");

        /// <summary>Tamanho em unidades decimais, como vem escrito na embalagem (1 GB = 1.000.000.000 bytes).</summary>
        public static string Bytes(long b)
        {
            if (b < 0) return "?";
            double v = b;
            if (v >= 1e12) return (v / 1e12).ToString("0.##", Br) + " TB";
            if (v >= 1e9) return (v / 1e9).ToString("0.#", Br) + " GB";
            if (v >= 1e6) return (v / 1e6).ToString("0", Br) + " MB";
            if (v >= 1e3) return (v / 1e3).ToString("0", Br) + " KB";
            return b + " bytes";
        }

        public static string Speed(double mbs)
        {
            if (mbs <= 0) return "-";
            return mbs.ToString(mbs >= 10 ? "0" : "0.0", Br) + " MB/s";
        }

        public static string Duration(TimeSpan t)
        {
            if (t.TotalSeconds < 1) return "menos de 1 s";
            if (t.TotalMinutes < 1) return ((int)t.TotalSeconds) + " s";
            if (t.TotalHours < 1) return ((int)t.TotalMinutes) + " min " + t.Seconds.ToString("00") + " s";
            return ((int)t.TotalHours) + " h " + t.Minutes.ToString("00") + " min";
        }

        public static string Eta(long remainingBytes, double mbs)
        {
            if (mbs <= 0.01) return "";
            double sec = remainingBytes / (mbs * 1e6);
            if (sec > 360000 || sec < 2) return "";
            return "faltam ~" + Duration(TimeSpan.FromSeconds(sec));
        }

        public static string Percent(double fraction)
        {
            return (fraction * 100).ToString("0.#", Br) + "%";
        }
    }

    /// <summary>Texto final mostrado ao usuario.</summary>
    public class ReportText
    {
        public Verdict Verdict;
        public string Title;
        public string Summary;
        public List<string> Lines = new List<string>();
    }
}
