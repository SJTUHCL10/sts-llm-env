using Forge.Core;

try
{
    if (args is ["config", var configPath])
    {
        AtomicStore.Write(Path.GetFullPath(configPath), new ForgeConfig());
        Console.WriteLine("Default config written (no credentials).");
    }
    else if (args is ["validate", var cardsPath])
    {
        var cards = Wire.Decode<CardBatch>(File.ReadAllText(cardsPath));
        foreach (var card in cards.Cards) CardValidator.Validate(card);
        Console.WriteLine($"Valid: {cards.Cards.Length} cards.");
    }
    else if (args is ["generate", var configuration, var observation, var output])
    {
        var config = Wire.Decode<ForgeConfig>(File.ReadAllText(configuration));
        config.Validate();
        var context = Wire.Decode<GenerationContext>(File.ReadAllText(observation));
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var generator = new OpenAiCardGenerator(http, config.Provider);
        var batch = await generator.GenerateAsync(PromptBuilder.Build(config, context), default);
        if (batch.Cards.Length != config.GeneratedCardsPerReward) throw new FormatException("Wrong card count.");
        foreach (var card in batch.Cards) CardValidator.Validate(card);
        AtomicStore.Write(Path.GetFullPath(output), batch);
        Console.WriteLine($"Generated {batch.Cards.Length} validated cards.");
    }
    else
    {
        Console.WriteLine("Neow's Company tool: config <path> | validate <cards.json> | generate <config.json> <context.json> <output.json>");
        return 2;
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Operation failed: " + ex.GetType().Name + ". Check files/provider; raw provider errors are suppressed.");
    return 1;
}
