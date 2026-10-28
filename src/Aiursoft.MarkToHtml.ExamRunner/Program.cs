using Aiursoft.MarkToHtml.AgentExam;
using Newtonsoft.Json;

if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project src/Aiursoft.MarkToHtml.ExamRunner (offline only; no arguments)");
    return 2;
}

try
{
    var executor = new ExamExecutor();
    var results = new List<Aiursoft.AgentKit.Evaluator.EvaluationCaseResult>();
    foreach (var scenario in Enum.GetValues<ExamScenario>())
    {
        var result = await executor.RunAsync(scenario);
        results.Add(result);
        Console.Error.WriteLine($"{result.CaseId}: {(result.Valid && result.Passed ? "PASS" : "FAIL")} " +
                                $"({result.Assertions.Count(assertion => assertion.Matched)}/{result.Assertions.Count} assertions)");
    }
    // stdout is machine-readable JSON; human status goes to stderr. Never writes reports.
    Console.WriteLine(JsonConvert.SerializeObject(results, Formatting.Indented));
    return results.All(result => result.Valid && result.Passed) ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Offline exam failed: {exception.GetType().Name}");
    return 1;
}
