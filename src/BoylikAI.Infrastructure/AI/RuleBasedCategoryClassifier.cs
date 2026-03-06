using BoylikAI.Domain.Enums;

namespace BoylikAI.Infrastructure.AI;

/// <summary>
/// Fast, deterministic rule-based classifier. Used as fallback and for AI confidence correction.
/// Keyword patterns sourced from Uzbek, Russian, and mixed-language informal writing.
/// </summary>
public sealed class RuleBasedCategoryClassifier
{
    private static readonly Dictionary<TransactionCategory, string[]> CategoryKeywords = new()
    {
        [TransactionCategory.Transport] = new[]
        {
            "avtobus", "bus", "taksi", "taxi", "uber", "yandex taxi", "metrо", "metro",
            "marshrutka", "poyezd", "train", "samolyot", "avia", "transport",
            "benzin", "benzine", "yoqilg'i", "fuel", "parking"
        },
        [TransactionCategory.Food] = new[]
        {
            "kafe", "cafe", "restoran", "restaurant", "ovqat", "food", "non", "bread",
            "go'sht", "meat", "sabzavot", "vegetable", "meva", "fruit", "sut", "milk",
            "tuxum", "egg", "chai", "tea", "kofe", "coffee", "pizza", "burger",
            "sho'rva", "osh", "lag'mon", "shashlik", "supermarket", "magnit", "korzinka",
            "uzum", "grip", "ichimlik", "drink"
        },
        [TransactionCategory.Shopping] = new[]
        {
            "do'kon", "shop", "market", "bozor", "bazaar", "kiyim", "clothes",
            "poyabzal", "shoes", "telefon", "phone", "kompyuter", "computer",
            "laptop", "planshет", "tablet", "elektronika", "electronics",
            "mebel", "furniture", "uy jihozlari", "household", "sovg'a", "gift"
        },
        [TransactionCategory.Bills] = new[]
        {
            "kommunal", "utility", "gaz", "gas", "elektr", "electric", "svet",
            "suv", "water", "internet", "wifi", "telefon to'lovi", "phone bill",
            "ijara", "rent", "kredit", "credit", "qarz", "debt", "soliq", "tax",
            "sug'urta", "insurance", "bank"
        },
        [TransactionCategory.Entertainment] = new[]
        {
            "kino", "cinema", "film", "movie", "konsert", "concert", "teatr", "theater",
            "o'yin", "game", "sport", "fitnes", "fitness", "gym", "zal",
            "sayohat", "travel", "dam olish", "vacation", "netflix", "spotify",
            "youtube premium", "o'yin-kulgi"
        },
        [TransactionCategory.Health] = new[]
        {
            "dorixona", "pharmacy", "apteka", "dori", "medicine", "shifokor", "doctor",
            "klinika", "clinic", "kasalxona", "hospital", "tahlil", "analysis",
            "vitamin", "analizlar", "tibbiy", "medical", "stomatolog", "dentist"
        },
        [TransactionCategory.Education] = new[]
        {
            "kurs", "course", "o'quv", "study", "kitob", "book", "universitet",
            "university", "maktab", "school", "ta'lim", "education", "repetitor",
            "tutor", "online kurs", "udemy", "coursera", "sertifikat", "certificate"
        },
        [TransactionCategory.Salary] = new[]
        {
            "oylik", "salary", "maosh", "ish haqi", "zарплата", "зарплата"
        },
        [TransactionCategory.Freelance] = new[]
        {
            "freelance", "frilanser", "buyurtma", "order", "loyiha", "project",
            "topshiriq", "task", "dizayn", "design", "dasturlash", "programming"
        }
    };

    public TransactionCategory Classify(string message)
    {
        var lower = message.ToLowerInvariant();
        var scores = new Dictionary<TransactionCategory, int>();

        foreach (var (category, keywords) in CategoryKeywords)
        {
            var matchCount = keywords.Count(k => lower.Contains(k));
            if (matchCount > 0)
                scores[category] = matchCount;
        }

        return scores.Count > 0
            ? scores.OrderByDescending(kvp => kvp.Value).First().Key
            : TransactionCategory.Other;
    }

    public TransactionCategory ClassifyOrDefault(string message, TransactionCategory aiCategory)
    {
        var ruleCategory = Classify(message);
        // Rule-based result takes precedence only if it's more specific than "Other"
        return ruleCategory != TransactionCategory.Other ? ruleCategory : aiCategory;
    }
}
