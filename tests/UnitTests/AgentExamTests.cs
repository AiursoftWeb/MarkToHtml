using Aiursoft.AgentKit.Evaluator;
using Aiursoft.MarkToHtml.AgentExam;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class AgentExamTests
{
    [TestMethod]
    [DataRow(ExamScenario.ReadOnly, "Original document", 0, 1)]
    [DataRow(ExamScenario.ApprovedWrite, "Edited document", 1, 1)]
    [DataRow(ExamScenario.RejectedWrite, "Original document", 0, 0)]
    public async Task OfflineScenarios_VerifyPersistenceAndDispatchBoundaries(ExamScenario scenario,
        string persisted, int persistenceDispatches, int toolCount)
    {
        var result = await new ExamExecutor().RunAsync(scenario);
        Assert.IsTrue(result.Valid, result.Error);
        Assert.IsTrue(result.Passed, string.Join("; ", result.Assertions.Where(a => !a.Matched).Select(a => a.Detail)));
        Assert.AreEqual(5, result.Assertions.Count);
        Assert.AreEqual(100d, result.Score);
        var evidence = result.Steps!.Single();
        Assert.AreEqual(persisted, evidence.State["PersistedDocumentContent"]!.Value<string>());
        Assert.AreEqual("FreshInMemoryDbContext", evidence.State["PersistedDocumentSource"]!.Value<string>());
        Assert.IsFalse(evidence.State["PersistenceVerified"]!.Value<bool>(), "Adapter memory is not persistence proof.");
        Assert.AreEqual(2, evidence.IterationCount);
        Assert.AreEqual(2, evidence.State["ModelInvocations"]!.Value<int>());
        Assert.AreEqual(persistenceDispatches, evidence.State["PersistenceDispatches"]!.Value<int>());
        Assert.AreEqual(toolCount, evidence.Tools.Count);
    }

    [TestMethod]
    public async Task WrongPersistedDocument_FailsEvenWhenFacadeMemoryLooksCorrect()
    {
        var result = await new ExamExecutor().RunAsync(ExamScenario.ApprovedWrite);
        var evidence = result.Steps!.Single();
        Assert.AreEqual("Edited document", evidence.State["DocumentContent"]!.Value<string>());
        evidence.State["PersistedDocumentContent"] = "Original document";
        var negative = new AssertionEvaluator().Evaluate(ExamExecutor.CreateCase(ExamScenario.ApprovedWrite),
            new EvaluationEvidence([evidence]));
        Assert.IsTrue(negative.Valid);
        Assert.IsFalse(negative.Passed);
        Assert.IsFalse(negative.Assertions.Single(a => a.Id == "persisted-document").Matched);
        Assert.AreEqual(4, negative.Assertions.Count(a => a.Matched));
    }
}
