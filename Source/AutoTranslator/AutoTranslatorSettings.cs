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

    /// <summary>凭据折叠区是否折叠（仅影响设置页渲染，不持久化）。</summary>
    public bool credentialsCollapsed;

    /// <summary>已启用的平台凭据列表（每个平台一条，由平台勾选状态增删）。</summary>
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

    /// <summary>某平台是否已启用。</summary>
    public bool HasPlatform(string platformId) =>
        credentials.FirstOrDefault(c => c.PlatformId == platformId)?.enabled ?? false;

    /// <summary>启用某平台：补齐凭据（若不存在），并标记为启用。</summary>
    public void EnablePlatform(string platformId)
    {
        PlatformCredential cred = credentials.FirstOrDefault(c => c.PlatformId == platformId);
        if (cred == null)
        {
            cred = new PlatformCredential { PlatformId = platformId };
            credentials.Add(cred);
        }
        cred.enabled = true;
    }

    /// <summary>禁用某平台：仅标记为禁用，保留凭据数据，避免再勾选时密钥丢失。</summary>
    public void DisablePlatform(string platformId)
    {
        PlatformCredential cred = credentials.FirstOrDefault(c => c.PlatformId == platformId);
        if (cred != null)
        {
            cred.enabled = false;
        }
    }

    /// <summary>获取某平台凭据，不存在返回 null。</summary>
    public PlatformCredential GetCredential(string platformId) =>
        credentials.FirstOrDefault(c => c.PlatformId == platformId);

    /// <summary>重置单个平台的凭据为默认（清空访问键与密钥）。</summary>
    public void ResetPlatform(string platformId)
    {
        PlatformCredential cred = credentials.FirstOrDefault(c => c.PlatformId == platformId);
        if (cred != null)
        {
            cred.AccessKey = string.Empty;
            cred.SecretKey = string.Empty;
            cred.ApiBaseUrl = string.Empty;
            cred.Model = string.Empty;
        }
    }

    /// <summary>确保所有已注册平台都有凭据对象（用于升级旧存档或新增平台时补齐）。</summary>
    public void EnsureAllPlatforms()
    {
        foreach (string platformId in PlatformRegistry.PlatformTypes.Keys)
        {
            if (credentials.All(c => c.PlatformId != platformId))
            {
                // 与 PlatformCredential.enabled 默认值一致（true），避免首次进入时所有平台被默认禁用导致 Dispatcher.Count == 0。
                credentials.Add(new PlatformCredential
                {
                    PlatformId = platformId,
                    enabled = platformId != OpenAICompatibleTranslate.PlatformNameKey
                });
            }
        }
    }

    /// <summary>恢复默认：清空凭据与所有开关。</summary>
    public void ResetToDefaults()
    {
        credentials.Clear();
        enableAutoTranslate = true;
        enableTranslationLog = false;
        enableRealtimeUpdate = false;
        credentialsCollapsed = false;
        AutoLog.Enabled = false;
        AutoTranslationStore.SetTranslationLogEnabled(false);
    }
}

/// <summary>
/// 单个平台的访问凭据。实现 IExposable 以便随 ModSettings 持久化到 {ModId}.xml。
/// 访问键/密钥字段名因平台而异（APP ID / AccessKeyId 等），统一以 AccessKey 承载。
/// </summary>
public class PlatformCredential : IExposable
{
    /// <summary>平台标志（PlatformNameKey，如 "AutoTranslator.Baidu"），同时作为引擎匹配的 PlatformId。</summary>
    public string PlatformId;

    /// <summary>该平台是否启用。取消勾选仅改此标记，不删除凭据，避免再勾选时密钥丢失。</summary>
    public bool enabled = true;

    /// <summary>存档用混淆串（仅落盘，代码不直接读）。</summary>
    private string accessKeyObf;

    /// <summary>存档用混淆串（仅落盘，代码不直接读）。</summary>
    private string secretKeyObf;

    /// <summary>OpenAI 兼容接口的地址和模型名；非密钥，直接持久化。</summary>
    public string ApiBaseUrl = string.Empty;
    public string Model = string.Empty;

    /// <summary>访问键输入框是否显示明文（运行时临时状态，不落盘）。</summary>
    public bool showAccessKey;

    /// <summary>密钥输入框是否显示明文（运行时临时状态，不落盘）。</summary>
    public bool showSecretKey;

    /// <summary>访问标识 / APP ID（百度为 appId，阿里为 AccessKeyId，火山为 AccessKey）。对外明文，内部混淆存储。</summary>
    public string AccessKey
    {
        get => Obfuscation.Decode(accessKeyObf);
        set => accessKeyObf = Obfuscation.Encode(value);
    }

    /// <summary>访问密钥 / SecretKey。对外明文，内部混淆存储。</summary>
    public string SecretKey
    {
        get => Obfuscation.Decode(secretKeyObf);
        set => secretKeyObf = Obfuscation.Encode(value);
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref PlatformId, "platformId");
        Scribe_Values.Look(ref enabled, "enabled", true);
        Scribe_Values.Look(ref accessKeyObf, "accessKey");
        Scribe_Values.Look(ref secretKeyObf, "secretKey");
        Scribe_Values.Look(ref ApiBaseUrl, "apiBaseUrl", string.Empty);
        Scribe_Values.Look(ref Model, "model", string.Empty);
    }
}
