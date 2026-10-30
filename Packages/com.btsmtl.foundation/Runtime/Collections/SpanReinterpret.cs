using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ThirdPersonSimulation
{
    // Unity .NET Standard 2.1 的 MemoryMarshal.Cast／Read／AsBytes 内部访问 typeof，Burst 拒绝编译；
    // 推进路径统一经此处以 GetReference、Unsafe.As 与 CreateSpan 重解释。
    public static class SpanReinterpret
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<TTo> Cast<TFrom, TTo>(Span<TFrom> source)
            where TFrom : unmanaged where TTo : unmanaged =>
            MemoryMarshal.CreateSpan(ref Unsafe.As<TFrom, TTo>(ref MemoryMarshal.GetReference(source)),
                Length<TFrom, TTo>(source.Length));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<TTo> Cast<TFrom, TTo>(ReadOnlySpan<TFrom> source)
            where TFrom : unmanaged where TTo : unmanaged =>
            MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<TFrom, TTo>(ref Unsafe.AsRef(in MemoryMarshal.GetReference(source))),
                Length<TFrom, TTo>(source.Length));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Read<T>(ReadOnlySpan<byte> source) where T : unmanaged
        {
            if (source.Length < Unsafe.SizeOf<T>())
                throw new ArgumentOutOfRangeException(nameof(source));
            return Unsafe.ReadUnaligned<T>(ref Unsafe.AsRef(in MemoryMarshal.GetReference(source)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<byte> AsBytes<T>(Span<T> source) where T : unmanaged => Cast<T, byte>(source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<byte> AsBytes<T>(ReadOnlySpan<T> source) where T : unmanaged => Cast<T, byte>(source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Length<TFrom, TTo>(int count) where TFrom : unmanaged where TTo : unmanaged =>
            checked((int)((long)count * Unsafe.SizeOf<TFrom>() / Unsafe.SizeOf<TTo>()));
    }
}
