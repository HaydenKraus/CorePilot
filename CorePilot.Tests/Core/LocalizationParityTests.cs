using CorePilot.App.Localization;
using Xunit;

namespace CorePilot.Tests.Core;

public class LocalizationParityTests
{
    public LocalizationParityTests() => LocalizationService.Instance.LoadPacks();

    [Fact]
    public void Packs_ContainBothLanguages()
    {
        Assert.NotEmpty(LocalizationService.Instance.GetKeys("en"));
        Assert.NotEmpty(LocalizationService.Instance.GetKeys("ru"));
    }

    [Fact]
    public void EveryEnglishKey_HasRussianTranslation()
    {
        var en = LocalizationService.Instance.GetPack("en");
        var ru = LocalizationService.Instance.GetPack("ru");

        var missing = en.Keys.Where(k => !ru.ContainsKey(k)).ToArray();

        Assert.True(missing.Length == 0,
            "Missing Russian keys: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryRussianKey_HasEnglishTranslation()
    {
        var en = LocalizationService.Instance.GetPack("en");
        var ru = LocalizationService.Instance.GetPack("ru");

        var missing = ru.Keys.Where(k => !en.ContainsKey(k)).ToArray();

        Assert.True(missing.Length == 0,
            "Missing English keys: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoValueIsEmpty()
    {
        foreach (var language in LocalizationService.SupportedLanguages)
        {
            var pack = LocalizationService.Instance.GetPack(language);
            var empty = pack.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToArray();
            Assert.True(empty.Length == 0,
                $"Empty {language} values: {string.Join(", ", empty)}");
        }
    }

    [Fact]
    public void SwitchingLanguage_ChangesRenderedText()
    {
        LocalizationService.Instance.SetLanguage("en");
        var english = LocalizationService.Instance["action.terminate"];

        LocalizationService.Instance.SetLanguage("ru");
        var russian = LocalizationService.Instance["action.terminate"];

        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.False(string.IsNullOrWhiteSpace(russian));
        Assert.NotEqual(english, russian);
    }

    [Fact]
    public void UnknownKey_ReturnsKeyItself()
    {
        LocalizationService.Instance.SetLanguage("en");
        Assert.Equal("this.key.does.not.exist", LocalizationService.Instance["this.key.does.not.exist"]);
    }
}
