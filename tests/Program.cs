using System.Text.Json;
using ComboHint;

var rules = HintRules.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combo_hint.config.json")));
var matcher = new HintMatcher(rules);
using var review = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "WeakVulnerableReview.json")));
var reviewedCards = review.RootElement.GetProperty("cards").EnumerateArray().ToArray();
using var remainingReview = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RemainingEffectsReview.json")));
var remainingCards = remainingReview.RootElement.GetProperty("cards").EnumerateArray().ToArray();
using var characterReview = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CharacterEffectsReview.json")));
var characterCards = characterReview.RootElement.GetProperty("cards").EnumerateArray().ToArray();
var tests = new (string Name, Action Run)[]
{
    ("caster-only effects stay excluded when a legacy config still lists them", () =>
    {
        var old = rules with { Groups = rules.Groups.Append(TriggerGroup.From("other", new() { "STRANGLE", "MONARCHS_GAZE" }, "#fff")).ToArray() };
        var oldMatcher = new HintMatcher(old);
        foreach (string id in new[] { "STRANGLE", "strangle", "MONARCHS_GAZE", "monarchs_gaze" })
            Equal(0, oldMatcher.Match(new[] { new HintCard(id, "Caster only+") }).Count);
        Equal(0, matcher.Match(new[] { new HintCard("MAD_SCIENCE", "Choking", DynamicHintEffects.Strangle) }).Count);
        True(!rules.Groups.Any(g => g.TriggerModelIds.Contains("STRANGLE") || g.TriggerModelIds.Contains("MONARCHS_GAZE")));
    }),
    ("dynamic variants retain distinct effects without adding ambiguous static IDs", () =>
    {
        var sapping = new HintCard("MAD_SCIENCE", "Mad Science", DynamicHintEffects.Weak | DynamicHintEffects.Vulnerable);
        var choking = new HintCard("MAD_SCIENCE", "Mad Science", DynamicHintEffects.Strangle);
        var matches = matcher.MatchCards(new[] { new HintCard("MAD_SCIENCE", "Mad Science"), sapping, sapping, choking });
        Equal(1, matches.Count);
        Sequence(new[] { sapping }, matches.Select(m => m.Card));
        var vulnerable = new HintCard("MAD_SCIENCE", "Mad Science", DynamicHintEffects.Vulnerable);
        var variants = matcher.Match(new[] { sapping, vulnerable });
        True(variants[0] != variants[1]);
        var timer = new BubbleDebouncer<string>();
        timer.EnterHand("p", true, new[] { matches[0].Trigger });
        True(timer.Advance(1).Single().HasNewMatches(variants));
    }),
    ("presentation retains source Model IDs while overlay and debounce projections stay unchanged", () =>
    {
        var cards = new[] { new HintCard("FLANKING", "夹击"), new HintCard("MALAISE", "萎靡+"), new HintCard("FLANKING", "夹击+") };
        var selected = matcher.MatchCards(cards);
        Sequence(new[] { "FLANKING", "MALAISE" }, selected.Select(m => m.Card.ModelId));
        Sequence(new[] { "夹击", "萎靡+" }, selected.Select(m => m.Card.Title));
        Sequence(matcher.Match(cards), selected.Select(m => m.Trigger));
        Equal(HintCategory.StrengthLoss, selected[1].Trigger.Category);
    }),
    ("reviewed enemy application lists exactly match the bilingual card catalog decisions", () =>
    {
        Equal(47, reviewedCards.Length);
        Equal(47, reviewedCards.Select(c => c.GetProperty("entry").GetString()).Distinct().Count());
        foreach (var (key, flag, count) in new[]
        {
            ("specialweaken", "applies_weak", 22),
            ("specialvulnerable", "applies_vulnerable", 25)
        })
        {
            var expected = reviewedCards.Where(c => c.GetProperty(flag).GetBoolean())
                .Select(c => c.GetProperty("entry").GetString()!).Order(StringComparer.Ordinal).ToArray();
            Equal(count, expected.Length);
            Sequence(expected, rules.Groups.Single(g => g.Key == key).TriggerModelIds.Order(StringComparer.Ordinal));
        }
        foreach (var card in reviewedCards.Where(c => c.GetProperty("exists").GetBoolean()))
        {
            Equal("CARD." + card.GetProperty("entry").GetString(), card.GetProperty("model_id").GetString());
            foreach (var field in new[] { "basic_en", "basic_zh", "upgraded_en", "upgraded_zh", "reason" })
                True(!string.IsNullOrWhiteSpace(card.GetProperty(field).GetString()));
        }
    }),
    ("reviewed application sources show their live localized card titles once, including upgrades", () =>
    {
        foreach (var card in reviewedCards)
        foreach (bool chinese in new[] { false, true })
        foreach (string suffix in new[] { "", "+" })
        {
            string id = card.GetProperty("entry").GetString()!;
            string title = card.GetProperty(chinese ? "title_zh" : "title_en").GetString() + suffix;
            var actual = matcher.Match(new[] { new HintCard(id, title) });
            bool expected = card.GetProperty("applies_weak").GetBoolean()
                || card.GetProperty("applies_vulnerable").GetBoolean() || id == "DEBILITATE";
            Equal(expected ? 1 : 0, actual.Count);
            if (expected) Equal(title, actual.Single().Text);
        }
    }),
    ("self debuffs, existing-status benefits and ambiguous variants never announce application", () =>
    {
        var excluded = new[] { "DOUBT", "BULLY", "COLOSSUS", "CRUELTY", "DISMANTLE", "DEBILITATE",
            "MOLTEN_FIST", "TRACKING", "VICIOUS", "MAD_SCIENCE", "FOLLOW_THROUGH" };
        var matches = matcher.Match(excluded.Select(id => new HintCard(id, id)));
        // DEBILITATE retains its independent 'other' reminder, without implying application.
        True(matches.Any(m => m.Text == "DEBILITATE"));
        Equal(1, matches.Count);
        Sequence(new[] { new MatchedTrigger("Dominate", "#FF6B6B") },
            matcher.Match(new[] { new HintCard("DOMINATE", "Dominate") }));
        Sequence(new[] { new MatchedTrigger("Go for the Eyes", "#6EC1FF") },
            matcher.Match(new[] { new HintCard("GO_FOR_THE_EYES", "Go for the Eyes") }));
        Sequence(new[] { new MatchedTrigger("Nail", "#6EC1FF") },
            matcher.Match(new[] { new HintCard("NAIL", "Nail") }));
        True(!reviewedCards.Single(c => c.GetProperty("entry").GetString() == "FOLLOW_THROUGH").GetProperty("exists").GetBoolean());
    }),
    ("configured Model IDs, group order, colors and duration", () =>
    {
        Equal(7, rules.Groups.Count);
        Equal(3.0, rules.BubbleDurationSeconds);
        var matches = matcher.Match(new[] { new HintCard("MALAISE", "萎靡") });
        Sequence(new[] { new MatchedTrigger("萎靡", "#E6A23C", HintCategory.StrengthLoss) }, matches);
        True(rules.Groups.Single(g => g.Key == "weakenattack").ColorHex != rules.Groups.Single(g => g.Key == "specialweaken").ColorHex);
        Sequence(new[] { new MatchedTrigger("Bash", "#FF6B6B") },
            matcher.Match(new[] { new HintCard("BASH", "Bash") }));
    }),
    ("case insensitive exact ID matching and no title inference", () =>
    {
        Equal(1, matcher.Match(new[] { new HintCard("bash", "Anything") }).Count);
        Equal(0, matcher.Match(new[] { new HintCard("BASH_PLUS", "Bash") }).Count);
        Equal(0, matcher.Match(new[] { new HintCard("UNKNOWN", "Apply Weak and Vulnerable") }).Count);
    }),
    ("duplicates collapse by ID while distinct cards sharing an effect retain their names", () =>
    {
        var cards = new[] { new HintCard("BASH", "Bash"), new HintCard("BASH", "Bash+"), new HintCard("BEAM_CELL", "Beam Cell") };
        Sequence(new[] { new MatchedTrigger("Bash", "#FF6B6B"), new MatchedTrigger("Beam Cell", "#FF6B6B") }, matcher.Match(cards));
        Sequence(new[] { new MatchedTrigger("Shockwave", "#6EC1FF") },
            matcher.Match(new[] { new HintCard("SHOCKWAVE", "Shockwave") }));
    }),
    ("lethal participates in the shared matcher with its original label color", () =>
    {
        var feed = new[] { new HintCard("FEED", "Feed") };
        Sequence(new[] { new MatchedTrigger("Lethal", "#FF0000", HintCategory.Lethal) }, matcher.Match(feed));
        Equal("FEED", matcher.MatchCards(feed).Single().Card.ModelId);
        Equal(7, rules.KillGroup.TriggerModelIds.Count);
    }),
    ("lethal bubbles announce new availability and ignore extra lethal source cards", () =>
    {
        var feed = matcher.Match(new[] { new HintCard("FEED", "Feed") });
        var echo = matcher.Match(new[] { new HintCard("ECHOING_SLASH", "Echoing Slash+") });
        var timer = new BubbleDebouncer<string>();
        timer.EnterHand("p1", true, Array.Empty<MatchedTrigger>());
        True(timer.Advance(1).Single().HasNewMatches(feed));
        timer.EnterHand("p1", true, feed);
        True(!timer.Advance(1).Single().HasNewMatches(echo));
        timer.EnterHand("p1", true, Array.Empty<MatchedTrigger>());
        True(!timer.Advance(1).Single().HasNewMatches(Array.Empty<MatchedTrigger>()));
        var custom = new HintMatcher(rules with { KillTitleColor = "#123456" });
        Equal("#123456", custom.Match(new[] { new HintCard("FEED", "F") }).Single().ColorHex);
    }),
    ("remaining effect lists exactly match reviewed descriptions and merge all multiplayer effects", () =>
    {
        Equal(66, remainingCards.Length);
        foreach (var (key, count) in new[] { ("weakenattack", 9), ("multiplayer", 41), ("other", 4), ("overlayKill", 7) })
        {
            var expected = remainingCards.Concat(characterCards).Where(c => c.GetProperty("groups").EnumerateArray().Any(g => g.GetString() == key))
                .Select(c => c.GetProperty("entry").GetString()!).Distinct().Order(StringComparer.Ordinal).ToArray();
            Equal(count, expected.Length);
            var group = key == "overlayKill" ? rules.KillGroup : rules.Groups.Single(g => g.Key == key);
            Sequence(expected, group.TriggerModelIds.Order(StringComparer.Ordinal));
        }
        True(rules.Groups.All(g => g.Key is not ("weakendefence" or "playerhelpbuff" or "playerhelpcards")));
        foreach (var card in remainingCards)
        {
            string id = card.GetProperty("entry").GetString()!;
            foreach (string language in new[] { "en", "zh" })
            {
                string title = card.GetProperty("title_" + language).GetString()!;
                var hand = new[] { new HintCard(id, title) };
                var groups = card.GetProperty("groups").EnumerateArray().Select(g => g.GetString()).ToArray();
                var actual = matcher.Match(hand);
                bool characterSource = characterCards.Any(c => c.GetProperty("entry").GetString() == id
                    && c.GetProperty("groups").GetArrayLength() > 0);
                Equal(groups.Length > 0 || characterSource ? 1 : 0, actual.Count);
                if (actual.Count == 0) continue;
                Equal(groups.Contains("overlayKill") ? "Lethal" : title, actual.Single().Text);
                Equal(groups.Contains("overlayKill") ? HintCategory.Lethal
                    : groups.Contains("weakenattack") ? HintCategory.StrengthLoss : HintCategory.Card, actual.Single().Category);
            }
        }
    }),
    ("character effects match reviewed enemy sources without treating consumers as applicators", () =>
    {
        Equal(29, characterCards.Length);
        foreach (var key in new[] { "poison", "doom" })
        {
            var expected = characterCards.Where(c => c.GetProperty("groups").EnumerateArray().Any(g => g.GetString() == key))
                .Select(c => c.GetProperty("entry").GetString()!).Order(StringComparer.Ordinal);
            Equal(10, expected.Count());
            Sequence(expected, rules.Groups.Single(g => g.Key == key).TriggerModelIds.Order(StringComparer.Ordinal));
        }
        foreach (var card in characterCards)
        {
            string id = card.GetProperty("entry").GetString()!;
            bool expected = card.GetProperty("groups").GetArrayLength() > 0 || id is "CONCOCT" or "UNDERWORLD";
            foreach (var language in new[] { "zh", "en" })
            foreach (var suffix in new[] { "", "+" })
            {
                string title = card.GetProperty("title_" + language).GetString() + suffix;
                Equal(expected ? 1 : 0, matcher.Match(new[] { new HintCard(id, title) }).Count);
            }
        }
        Equal(rules.EffectColor("DEBILITATE_POWER"), rules.EffectColor("DISLIKE_YOU_POWER"));
        Equal("#78C850", rules.EffectColor("POISON_POWER"));
        Equal("#B780E6", rules.EffectColor("DOOM_POWER"));
    }),
    ("strength grouping takes precedence independently of configuration order", () =>
    {
        var reordered = rules with { Groups = rules.Groups.Reverse().ToArray() };
        var match = new HintMatcher(reordered).Match(new[] { new HintCard("MALAISE", "Malaise+") });
        Sequence(new[] { new MatchedTrigger("Malaise+", "#E6A23C", HintCategory.StrengthLoss) }, match);
    }),
    ("legacy rule format, empty groups and duration bounds", () =>
    {
        var parsed = HintRules.Parse("""
            {"triggerModelIds_a":[" BASH ","bash",null,5,""],"color_a":"ff0000",
             "triggerModelIds_empty":[],"overlayKillTriggerTexts":["FEED"],"bubbleDurationSeconds":-1}
            """);
        Equal(1, parsed.Groups.Count);
        Equal(1, parsed.Groups[0].TriggerModelIds.Count);
        Equal("#ff0000", parsed.Groups[0].ColorHex);
        Equal(0.3, parsed.BubbleDurationSeconds);
        True(parsed.KillGroup.TriggerModelIds.Contains("FEED"));
        Equal(10.0, HintRules.Parse("{\"bubbleDurationSeconds\":100}").BubbleDurationSeconds);
    }),
    ("continuous draw waits for quiet period and emits once", () =>
    {
        var timer = new BubbleDebouncer<string>();
        timer.EnterHand("p1", true, Array.Empty<MatchedTrigger>());
        Equal(0, timer.Advance(0.2).Count);
        timer.EnterHand("p1", false, Array.Empty<MatchedTrigger>());
        Equal(0, timer.Advance(0.2).Count);
        Equal(1, timer.Advance(0.11).Count);
        Equal(0, timer.Advance(1).Count);
    }),
    ("unmatched entries do not start bubbles; players have separate windows", () =>
    {
        var timer = new BubbleDebouncer<string>();
        timer.EnterHand("p1", false, Array.Empty<MatchedTrigger>());
        Equal(0, timer.Advance(1).Count);
        timer.EnterHand("p1", true, Array.Empty<MatchedTrigger>());
        timer.Advance(0.2);
        timer.EnterHand("p2", true, Array.Empty<MatchedTrigger>());
        Sequence(new[] { "p1" }, timer.Advance(0.11).Select(b => b.Key));
        Sequence(new[] { "p2" }, timer.Advance(0.2).Select(b => b.Key));
    }),
    ("window baseline survives additional draws and ignores hand ordering", () =>
    {
        var a = new MatchedTrigger("Weak", "#123456");
        var b = new MatchedTrigger("Vulnerable", "#654321");
        var timer = new BubbleDebouncer<string>();
        timer.EnterHand("p1", true, new[] { a, b });
        timer.EnterHand("p1", true, Array.Empty<MatchedTrigger>());
        var due = timer.Advance(1).Single();
        True(due.Baseline.SetEquals(new[] { b, a, a }));
        True(!due.Baseline.SetEquals(new[] { a }));
        True(!due.HasNewMatches(new[] { b, a }));
        True(!due.HasNewMatches(new[] { a })); // Removing a pre-existing effect is not a new effect.
        True(!due.HasNewMatches(Array.Empty<MatchedTrigger>()));
        True(due.HasNewMatches(new[] { a, new MatchedTrigger("New card", "#fff") }));
    }),
    ("clearing on turn, setting or combat boundary cancels pending work", () =>
    {
        var timer = new BubbleDebouncer<string>();
        timer.EnterHand("p1", true, Array.Empty<MatchedTrigger>());
        timer.Clear();
        Equal(0, timer.Advance(10).Count);
        var newBaseline = new[] { new MatchedTrigger("Weak", "#fff") };
        timer.EnterHand("p1", true, newBaseline);
        True(timer.Advance(1).Single().Baseline.SetEquals(newBaseline));
    }),
    ("legacy settings preserve explicit false and ignore unrelated or invalid fields", () =>
    {
        var settings = LegacySettings.Parse("""
            {"comboHintEnabled":false,"enableSinglePlayerHint":true,"bubbleHintEnabled":false,"id":"ComboHint"}
            """);
        Equal<bool?>(false, settings.Overlay);
        Equal<bool?>(true, settings.SinglePlayer);
        Equal<bool?>(false, settings.Bubbles);
        Equal<bool?>(null, LegacySettings.Parse("{\"comboHintEnabled\":\"false\"}").Overlay);
        bool threw = false;
        try { LegacySettings.Parse("{"); } catch (JsonException) { threw = true; }
        True(threw);
    })
};

int failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static void True(bool value) { if (!value) throw new Exception("Expected true."); }
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
}
static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual)) throw new Exception($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
}
