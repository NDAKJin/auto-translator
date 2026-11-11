using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

public class AutoTranslatorSettings : ModSettings
{
    // 基本设置：三个开关（持久化到 ModConfigs.xml，不进 git）
    // 开启动态翻译：拦截无 key 文本做动态翻译，有 key 的仍由系统管理
    public bool enableAutoTranslate = true;
    // 启用翻译记录：为无 key 字符串自动创建 key，并拦截有 key 但未翻译的，落盘到当前语言包 auto.xml
    public bool enableTranslationLog;
    // 启用实时翻译：翻译完成后实时回写到界面/文本
    public bool enableRealtimeUpdate;

    /// <summary>保留列表格式以读取旧版本的 OpenAI 凭据。</summary>
    public List<PlatformCredential> credentials = new();

    public override void ExposeData()
    {
        Scribe_Values.Look(ref enableAutoTranslate, "enableAutoTranslate", true);
        Scribe_Values.Look(ref enableTranslationLog, "enableTranslationLog", false);
        Scribe_Values.Look(ref enableRealtimeUpdate, "enableRealtimeUpdate", false);
        Scribe_Collections.Look(ref credentials, "credentials", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
        {
            AutoTranslationStore.SetTranslationLogEnabled(enableTranslationLog);
            AutoLog.Enabled = enableTranslationLog;
        }
    }

    /// <summary>获取 OpenAI 兼容接口的凭据。</summary>
    public PlatformCredential GetCredential(string platformId) =>
        credentials?.FirstOrDefault(c => c?.PlatformId == platformId);

    /// <summary>重置单个平台的凭据为默认（清空访问键与密钥）。</summary>
    public void ResetPlatform(string platformId)
    {
        PlatformCredential cred = credentials.FirstOrDefault(c => c.PlatformId == platformId);
        if (cred != null)
        {
            cred.AccessKey = string.Empty;
            cred.ApiBaseUrl = string.Empty;
            cred.Model = string.Empty;
        }
    }

    /// <summary>迁移旧设置，只保留已有的 OpenAI 凭据。</summary>
    public void EnsureOpenAIPlatform()
    {
        if (credentials == null) credentials = new List<PlatformCredential>();
        credentials.RemoveAll(c => c == null || c.PlatformId != OpenAICompatibleTranslate.PlatformNameKey);
        if (credentials.Count == 0)
        {
            credentials.Add(new PlatformCredential { PlatformId = OpenAICompatibleTranslate.PlatformNameKey });
        }
        if (credentials.Count > 1) credentials.RemoveRange(1, credentials.Count - 1);
    }

    /// <summary>恢复默认：清空凭据与所有开关。</summary>
    public void ResetToDefaults()
    {
        credentials.Clear();
        enableAutoTranslate = true;
        enableTranslationLog = false;
        enableRealtimeUpdate = false;
        EnsureOpenAIPlatform();
        AutoLog.Enabled = false;
        AutoTranslationStore.SetTranslationLogEnabled(false);
    }
}

/// <summary>
/// OpenAI 兼容接口凭据。保留旧存档的列表结构以迁移既有配置。
/// </summary>
public class PlatformCredential : IExposable
{
    /// <summary>平台标志，兼容旧设置文件。</summary>
    public string PlatformId;

    /// <summary>存档用混淆串（仅落盘，代码不直接读）。</summary>
    private string accessKeyObf;

    /// <summary>OpenAI 兼容接口的地址和模型名；非密钥，直接持久化。</summary>
    public string ApiBaseUrl = string.Empty;
    public string Model = string.Empty;

    /// <summary>访问键输入框是否显示明文（运行时临时状态，不落盘）。</summary>
    public bool showAccessKey;

    /// <summary>API Key，对外明文，内部混淆存储。</summary>
    public string AccessKey
    {
        get => Obfuscation.Decode(accessKeyObf);
        set => accessKeyObf = Obfuscation.Encode(value);
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref PlatformId, "platformId");
        Scribe_Values.Look(ref accessKeyObf, "accessKey");
        Scribe_Values.Look(ref ApiBaseUrl, "apiBaseUrl", string.Empty);
        Scribe_Values.Look(ref Model, "model", string.Empty);
    }
}
