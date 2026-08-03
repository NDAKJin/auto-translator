namespace nomadicooer.AutoTranslator;

/// <summary>
/// 软件自身显示的翻译 key 常量表。
/// 仅存放 key 名（字符串常量），实际的中英文值仍只存在于 Languages 下的 AutoTranslator.xml。
/// 代码中所有需要显示的字符串都应引用本表的常量，禁止出现裸字符串或硬编码显示文本。
/// </summary>
public static class TranslateKeys
{
  public const string SettingsCategory = "AutoTranslator.SettingsCategory";
  public const string BasicInfo = "AutoTranslator.BasicInfo";
  public const string ActiveLanguage = "AutoTranslator.ActiveLanguage";
  public const string DefaultLanguage = "AutoTranslator.DefaultLanguage";
  public const string EnableAutoTranslate = "AutoTranslator.EnableAutoTranslate";
  public const string EnableTranslationLog = "AutoTranslator.EnableTranslationLog";
  public const string EnableRealtimeUpdate = "AutoTranslator.EnableRealtimeUpdate";
  public const string VolcengineAccessKey = "AutoTranslator.VolcengineAccessKey";
  public const string VolcengineSecretKey = "AutoTranslator.VolcengineSecretKey";
  public const string AliyunAccessKeyId = "AutoTranslator.AliyunAccessKeyId";
  public const string AliyunAccessKeySecret = "AutoTranslator.AliyunAccessKeySecret";
  public const string BaiduAppId = "AutoTranslator.BaiduAppId";
  public const string BaiduSecretKey = "AutoTranslator.BaiduSecretKey";
  public const string Volcengine = "AutoTranslator.Volcengine";
  public const string Baidu = "AutoTranslator.Baidu";
  public const string Aliyun = "AutoTranslator.Aliyun";
  public const string PlatformList = "AutoTranslator.PlatformList";
  public const string CredentialTable = "AutoTranslator.CredentialTable";
  public const string AccessKey = "AutoTranslator.AccessKey";
  public const string SecretKey = "AutoTranslator.SecretKey";
  public const string ResetDefaults = "AutoTranslator.ResetDefaults";
  public const string SaveSettings = "AutoTranslator.SaveSettings";
  public const string TranslateAll = "AutoTranslator.TranslateAll";
}
