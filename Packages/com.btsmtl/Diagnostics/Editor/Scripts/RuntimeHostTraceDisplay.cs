using System;

namespace BTSMTL.Diagnostics.Editor
{
    // 宿主事件的显示文字只在编辑器读取时由宿主登记的格式化器生成，运行时不拼字符串。
    public interface IRuntimeHostTraceFormatter
    {
        string Status(in RuntimeTracePayload payload);
        string Detail(in RuntimeTracePayload payload);
    }

    public static class RuntimeHostTraceDisplay
    {
        static readonly IRuntimeHostTraceFormatter[] s_Formatters = new IRuntimeHostTraceFormatter[RuntimeHostTraceKinds.Capacity];

        public static void Register(RuntimeTraceEventKind kind, IRuntimeHostTraceFormatter formatter)
        {
            if (!RuntimeHostTraceKinds.IsHost(kind))
                throw new ArgumentOutOfRangeException(nameof(kind), "只能为宿主事件登记格式化器。");
            int index = (int)kind - RuntimeHostTraceKinds.First;
            IRuntimeHostTraceFormatter existing = s_Formatters[index];
            if (existing != null && existing.GetType() != formatter.GetType())
                throw new InvalidOperationException($"宿主诊断事件 {index} 已登记格式化器 {existing.GetType().Name}。");
            s_Formatters[index] = formatter ?? throw new ArgumentNullException(nameof(formatter));
        }

        public static string Status(RuntimeTraceEventKind kind, in RuntimeTracePayload payload)
        {
            if (TryGet(kind, out IRuntimeHostTraceFormatter formatter))
                return formatter.Status(in payload);
            return payload.Status;
        }

        public static string Detail(RuntimeTraceEventKind kind, in RuntimeTracePayload payload)
        {
            if (TryGet(kind, out IRuntimeHostTraceFormatter formatter))
                return formatter.Detail(in payload);
            return payload.Detail;
        }

        static bool TryGet(RuntimeTraceEventKind kind, out IRuntimeHostTraceFormatter formatter)
        {
            formatter = RuntimeHostTraceKinds.IsHost(kind) ? s_Formatters[(int)kind - RuntimeHostTraceKinds.First] : null;
            return formatter != null;
        }
    }
}
