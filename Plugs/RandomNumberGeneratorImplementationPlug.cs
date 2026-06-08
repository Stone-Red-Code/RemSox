using System;
using Cosmos.Build.API.Attributes;

namespace MyKernel.Plugs;

[Plug("System.Security.Cryptography.RandomNumberGeneratorImplementation")]
public static unsafe class RandomNumberGeneratorImplementationImpl
{
    // Cryptographic stream generator based on ChaCha20.
    // Initial entropy is limited at boot; depends on hardware events.
    private static uint[] key = new uint[8];
    private static uint[] counter = new uint[4];
    private static uint[] state = new uint[16];

    private static ulong e0, e1, e2, e3;
    private static int entropyCounter;
    private static bool initialized = false;

    private static readonly uint[] constants =
    {
        0x61707865, 0x3320646E, 0x79622D32, 0x6B206574
    };

    private static uint RotL(uint x, int n)
    {
        return (x << n) | (x >> (32 - n));
    }

    private static void QR(ref uint a, ref uint b, ref uint c, ref uint d)
    {
        a += b; d ^= a; d = RotL(d, 16);
        c += d; b ^= c; b = RotL(b, 12);
        a += b; d ^= a; d = RotL(d, 8);
        c += d; b ^= c; b = RotL(b, 7);
    }

    private static void ChaChaBlock(uint[] output)
    {
        uint[] x = new uint[16];

        for (int i = 0; i < 4; i++) x[i] = constants[i];
        for (int i = 0; i < 8; i++) x[4 + i] = key[i];
        for (int i = 0; i < 4; i++) x[12 + i] = counter[i];
        for (int i = 0; i < 16; i++) state[i] = x[i];

        for (int i = 0; i < 10; i++)
        {
            QR(ref x[0], ref x[4], ref x[8], ref x[12]);
            QR(ref x[1], ref x[5], ref x[9], ref x[13]);
            QR(ref x[2], ref x[6], ref x[10], ref x[14]);
            QR(ref x[3], ref x[7], ref x[11], ref x[15]);

            QR(ref x[0], ref x[5], ref x[10], ref x[15]);
            QR(ref x[1], ref x[6], ref x[11], ref x[12]);
            QR(ref x[2], ref x[7], ref x[8], ref x[13]);
            QR(ref x[3], ref x[4], ref x[9], ref x[14]);
        }

        for (int i = 0; i < 16; i++)
            output[i] = x[i] + state[i];

        counter[0]++;
        if (counter[0] == 0)
        {
            counter[1]++;
            if (counter[1] == 0)
            {
                counter[2]++;
                if (counter[2] == 0) counter[3]++;
            }
        }
    }

    private static void Mix(ulong v)
    {
        e0 ^= v;
        e0 *= 0x9E3779B97F4A7C15UL;
        e1 ^= e0 ^ (e0 >> 30);
        e1 *= 0xBF58476D1CE4E5B9UL;
        e2 ^= e1 ^ (e1 >> 27);
        e2 *= 0x94D049BB133111EBUL;
        e3 ^= e2 ^ (e2 >> 31);
    }

    private static void Reseed()
    {
        key[0] ^= (uint)e0; key[1] ^= (uint)(e0 >> 32);
        key[2] ^= (uint)e1; key[3] ^= (uint)(e1 >> 32);
        key[4] ^= (uint)e2; key[5] ^= (uint)(e2 >> 32);
        key[6] ^= (uint)e3; key[7] ^= (uint)(e3 >> 32);

        counter[0] ^= (uint)(e0 >> 16);
        counter[1] ^= (uint)(e1 >> 16);
        counter[2] ^= (uint)(e2 >> 16);
        counter[3] ^= (uint)(e3 >> 16);

        e0 = 0; e1 = 0; e2 = 0; e3 = 0;

        bufferIndex = 16;
    }

    private static void MaybeReseed()
    {
        if (++entropyCounter % 64 == 0)
            Reseed();
    }

    private static ulong ReadTSC()
    {
        return (ulong)Environment.TickCount;
    }

    private static void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        const ulong BUILD_NONCE = 0xDEADBEEFCAFEBABEUL;

        ulong heapBits;
        fixed (byte* p = new byte[1])
            heapBits = (ulong)p;

        Mix(BUILD_NONCE);
        Mix(ReadTSC());
        Mix(heapBits);
        Mix(ReadTSC() ^ (heapBits << 17));

        Reseed();
    }

    private static readonly Lock _lock = new();

    private static uint[] buffer = new uint[16];
    private static int bufferIndex = 16;

    private static uint NextUInt32()
    {
        EnsureInitialized();

        if (bufferIndex >= 16)
        {
            ChaChaBlock(buffer);
            bufferIndex = 0;

            key[0] ^= buffer[0];
            key[1] ^= buffer[1];
            key[2] ^= buffer[2];
            key[3] ^= buffer[3];
            key[4] ^= buffer[4];
            key[5] ^= buffer[5];
            key[6] ^= buffer[6];
            key[7] ^= buffer[7];

            ChaChaBlock(buffer);
        }

        return buffer[bufferIndex++];
    }

    private static int byteShift = 0;
    private static uint byteWord = 0;

    private static byte NextByte()
    {
        if (byteShift == 0)
        {
            byteWord = NextUInt32();
            byteShift = 32;
        }

        byteShift -= 8;
        return (byte)(byteWord >> byteShift);
    }

    public static void AddMouseEntropy(int dx, int dy, int dz, int x, int y)
    {
        ulong tsc = ReadTSC();

        lock (_lock)
        {
            Mix((ulong)dx);
            Mix((ulong)dy);
            Mix((ulong)dz);
            Mix((ulong)x ^ ((ulong)y << 32));
            Mix(tsc);
            MaybeReseed();
        }
    }

    public static void AddKeyboardEntropy(uint scanCode, uint flags, char keyChar)
    {
        ulong tsc = ReadTSC();

        lock (_lock)
        {
            Mix(scanCode);
            Mix(flags);
            Mix((ulong)keyChar);
            Mix(tsc);
            MaybeReseed();
        }
    }

    [PlugMember]
    public static void FillSpan(Span<byte> data)
    {
        lock (_lock)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] = NextByte();
        }
    }

    [PlugMember]
    public static void GetBytes(byte* bufferPtr, int count)
    {
        lock (_lock)
        {
            for (int i = 0; i < count; i++)
                bufferPtr[i] = NextByte();
        }
    }
}