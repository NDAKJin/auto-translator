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
  public const string ApiBaseUrl = "AutoTranslator.ApiBaseUrl";
  public const string Model = "AutoTranslator.Model";
  public const string ApiKey = "AutoTranslator.ApiKey";
  public const string ResetDefaults = "AutoTranslator.ResetDefaults";
  public const string SaveSettings = "AutoTranslator.SaveSettings";
  public const string TestConnection = "AutoTranslator.TestConnection";
  public const string TestConnectionRunning = "AutoTranslator.TestConnectionRunning";
  public const string TestConnectionSuccess = "AutoTranslator.TestConnectionSuccess";
  public const string TestConnectionFailure = "AutoTranslator.TestConnectionFailure";
  public const string ShowPassword = "AutoTranslator.ShowPassword";
  public const string HidePassword = "AutoTranslator.HidePassword";
  public const string TranslationFilePath = "AutoTranslator.TranslationFilePath";
  public const string TranslationFilePathLabel = "AutoTranslator.TranslationFilePathLabel";
  public const string OpenXmlFile = "AutoTranslator.OpenXmlFile";
  public const string ClearTranslationCache = "AutoTranslator.ClearTranslationCache";
  public const string ConfirmClearTranslationCache = "AutoTranslator.ConfirmClearTranslationCache";
}
