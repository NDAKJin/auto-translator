using System;
using System.Collections.Generic;
using System.Text;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 翻译接口调用统计（底层工具，无上层依赖）：按平台名累计「实际发出的翻译请求次数」及成功/失败数，
/// 另记录引擎侧后台任务的提交与完成数，用于判断「已翻译完毕后是否仍有后台请求在跑」。
/// 仅内存计数，进程退出即清零；线程安全（后台线程池并发调用）。
/// </summary>
public static class TranslateStats
{
    private sealed class Counter
    {
        public int Total;
        public int Success;
        public int Fail;
    }

    private static readonly Dictionary<string, Counter> counters = new();
    private static readonly object statsLock = new();

    // 后台翻译任务：已提交（入队）与已完成数，二者之差即当前仍在跑的后台任务数。
    private static int taskSubmitted;
    private static int taskFinished;

    /// <summary>记录一次对指定平台实际发出的翻译请求及其结果。由调度器在每次调用平台后调用。</summary>
    public static void RecordCall(string platformName, bool success)
    {
        string name = string.IsNullOrEmpty(platformName) ? "(unknown)" : platformName;
        lock (statsLock)
        {
            if (!counters.TryGetValue(name, out Counter c))
            {
                c = new Counter();
                counters[name] = c;
            }
            c.Total++;
            if (success)
            {
                c.Success++;
            }
            else
            {
                c.Fail++;
            }
        }
    }

    /// <summary>登记一个后台翻译任务已提交。</summary>
    public static void TaskSubmitted()
    {
        lock (statsLock)
        {
            taskSubmitted++;
        }
    }

    /// <summary>登记一个后台翻译任务已结束（无论成功、失败或异常）。</summary>
    public static void TaskFinished()
    {
        lock (statsLock)
        {
            taskFinished++;
        }
    }

    /// <summary>当前仍在后台运行（已提交未完成）的翻译任务数。用于检查「翻译完了是否还在后台跑」。</summary>
    public static int RunningTasks
    {
        get
        {
            lock (statsLock)
            {
                return taskSubmitted - taskFinished;
            }
        }
    }

    /// <summary>所有平台累计的实际请求总次数。</summary>
    public static int TotalCalls
    {
        get
        {
            lock (statsLock)
            {
                int sum = 0;
                foreach (Counter c in counters.Values)
                {
                    sum += c.Total;
                }
                return sum;
            }
        }
    }

    /// <summary>生成一行可读的统计摘要：各平台调用次数（成功/失败）+ 后台任务提交/完成/在跑。</summary>
    public static string BuildSummary()
    {
        StringBuilder sb = new();
        lock (statsLock)
        {
            sb.Append("翻译接口调用统计：");
            if (counters.Count == 0)
            {
                sb.Append("（无任何平台请求）");
            }
            else
            {
                List<string> names = new(counters.Keys);
                names.Sort(StringComparer.Ordinal);
                foreach (string name in names)
                {
                    Counter c = counters[name];
                    sb.Append($" [{name} 共{c.Total}次 成功{c.Success} 失败{c.Fail}]");
                }
            }
            sb.Append($" | 后台任务 提交{taskSubmitted} 完成{taskFinished} 在跑{taskSubmitted - taskFinished}");
        }
        return sb.ToString();
    }

    /// <summary>把统计摘要输出到日志。</summary>
    public static void LogSummary()
    {
        AutoLog.Info(BuildSummary());
    }

    /// <summary>清零所有统计（供设置页手动重置或重新开始一轮观测）。</summary>
    public static void Reset()
    {
        lock (statsLock)
        {
            counters.Clear();
            taskSubmitted = 0;
            taskFinished = 0;
        }
    }
}
