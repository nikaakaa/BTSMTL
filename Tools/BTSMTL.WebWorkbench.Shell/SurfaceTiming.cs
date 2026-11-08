using System.Diagnostics;
using System.Text.Json;

namespace BTSMTL.WebWorkbench.Shell;

// 诊断开关 --surface-timing <JSON 路径>：网页每画完一帧共享画面区回报该帧的采集与写完时刻（QueryPerformanceCounter），
// 壳按收到回报的时刻统计 采集→画完、写完→画完 的延迟与按序号算的丢帧，每 120 帧覆盖写一次汇总。
sealed class SurfaceTiming(string path)
{
    readonly List<double> m_FromCapture = [];
    readonly List<double> m_FromWrite = [];
    readonly List<double> m_Interval = [];
    long m_FirstSequence = -1, m_LastSequence = -1, m_LastDrawn;

    public void Record(long sequence, long captured, long written)
    {
        long now = Stopwatch.GetTimestamp();
        double milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
        m_FromCapture.Add(milliseconds(now - captured));
        m_FromWrite.Add(milliseconds(now - written));
        if (m_LastDrawn != 0)
            m_Interval.Add(milliseconds(now - m_LastDrawn));
        m_LastDrawn = now;
        if (m_FirstSequence < 0)
            m_FirstSequence = sequence;
        m_LastSequence = sequence;
        if (m_FromCapture.Count % 120 == 0)
            Write();
    }

    public void Write()
    {
        if (m_FromCapture.Count == 0)
            return;
        static object Summary(List<double> values)
        {
            double[] sorted = [.. values.Order()];
            double at(double quantile) => sorted[Math.Min(sorted.Length - 1, (int)(quantile * sorted.Length))];
            return new { count = sorted.Length, p50 = at(0.5), p95 = at(0.95), max = sorted[^1] };
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            drawn = m_FromCapture.Count,
            sequenceSpan = m_LastSequence - m_FirstSequence + 1,
            captureToDrawnMs = Summary(m_FromCapture),
            writtenToDrawnMs = Summary(m_FromWrite),
            drawIntervalMs = m_Interval.Count == 0 ? null : Summary(m_Interval)
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
