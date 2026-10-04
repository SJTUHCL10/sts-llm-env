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
    else if (args is ["prompt", var promptConfiguration, var promptObservation, var promptOutput])
    {
        var config = Wire.Decode<ForgeConfig>(File.ReadAllText(promptConfiguration));
        config.Validate();
        var context = Wire.Decode<GenerationContext>(File.ReadAllText(promptObservation));
        var prompt = PromptBuilder.Build(config, context);
        AtomicStore.Write(Path.GetFullPath(promptOutput), prompt);
        Console.WriteLine($"Prompt: {prompt.System.Length + prompt.User.Length} characters (no provider call).");
    }
    else if (args is ["generate", var configuration, var observation, var output])
    {
        var config = Wire.Decode<ForgeConfig>(File.ReadAllText(configuration));
        config.Validate();
        var context = Wire.Decode<GenerationContext>(File.ReadAllText(observation));
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var generator = new OpenAiCardGenerator(http, config.Provider, response =>
        {
            if (config.RecordGeneration) AtomicStore.Write(Path.GetFullPath(output) + ".response.local.json",
                response with { ReasoningContent = config.RecordGenerationReasoning ? response.ReasoningContent : null });
        });
        var batch = await generator.GenerateAsync(PromptBuilder.Build(config, context), default);
        if (batch.Cards.Length != config.GeneratedCardsPerReward) throw new FormatException("Wrong card count.");
        var mechanics = CharacterMechanics.FromRun(context.Run);
        foreach (var card in batch.Cards) mechanics.Validate(card);
        AtomicStore.Write(Path.GetFullPath(output), batch);
        Console.WriteLine($"Generated {batch.Cards.Length} validated cards.");
    }
    else
    {
        Console.WriteLine("Neow's Company tool: config <path> | validate <cards.json> | prompt <config.json> <context.json> <output.json> | generate <config.json> <context.json> <output.json>");
        return 2;
    }
    return 0;
}
catch (GenerationFailureException ex)
{
    Console.Error.WriteLine("Generation failed: " + ex.Reason + ".");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Operation failed: " + ex.GetType().Name + ". Check files/provider; raw provider errors are suppressed.");
    return 1;
}
