using ChatGPTRevit;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private static int passed;
    private static void Check(bool ok, string name)
    { if (!ok) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    private static void Reject(Action action, string name)
    {
        try { action(); } catch (Exception) { Check(true, name); return; }
        throw new Exception("FAIL: " + name);
    }
    [STAThread]
    private static async Task Main()
    {
        var c = new TraceCalibration { A = new() { X = 0, Y = 1000 }, B = new() { X = 1000, Y = 1000 },
            ImageWidth = 2000, ImageHeight = 1000, RealLengthMm = 10000 };
        var p = c.Convert(1000, 0);
        Check(Math.Abs(p.X - 10000) < .001 && Math.Abs(p.Y - 5000) < .001, "non-square image calibration / Y inversion");
        c.RotationDegrees = 90; c.OriginX = 200;
        p = c.Convert(1000, 1000);
        Check(Math.Abs(p.X - 200) < .001 && Math.Abs(p.Y - 10000) < .001, "origin and rotation");
        c.B = c.A;
        Reject(() => c.Convert(0, 0), "zero-length reference rejected");
        c.B = new() { X = 1000, Y = 1000 }; c.RotationDegrees = 0;
        var trace = new ImageTrace { Segments = new()
        {
            new() { Key = "w1", X1 = 0, Y1 = 1000, X2 = 1000, Y2 = 1000, Confidence = .9 },
            new() { Key = "d1", Kind = "door", HostKey = "w1", X1 = 400, Y1 = 1000, X2 = 500, Y2 = 1000, Confidence = .9 }
        } };
        var plan = TraceConverter.ToPlan(trace, c, "Térreo", 123, 234, 0, 2800);
        Check(plan.Actions.Count == 2 && plan.Actions[1].HostKey == "w1", "wall/opening ordering and host reference");
        trace.Segments[0].Include = false;
        Reject(() => TraceConverter.ToPlan(trace, c, "Térreo", 123, 234, 0, 2800), "orphan opening rejected");
        trace.Segments[0].Include = true;
        trace.Segments[0].X1 = 1001;
        Reject(() => TraceConverter.Validate(trace), "out-of-image coordinate rejected");
        Reject(() => EditPlanParser.Parse("{\"actions\":[{\"operation\":\"execute_code\"}]}"), "arbitrary code action rejected");
        Reject(() => EditPlanParser.Parse("{\"actions\":[],\"unexpected\":1}"), "unknown JSON field rejected");
        Reject(() => EditPlanParser.Parse("{\"actions\":[{\"operation\":\"place_door\",\"typeId\":1,\"levelName\":\"L\"}]}"), "unhosted door rejected");
        Reject(() => EditPlanParser.Parse("{\"actions\":[{\"operation\":\"move\",\"elementId\":1,\"dxMm\":1e100}]}"), "extreme transform rejected");
        Check(EditPlanParser.Parse("{\"actions\":[{\"operation\":\"move\",\"elementId\":5000000000,\"dxMm\":200}]}").Actions[0].ElementId == 5000000000, "64-bit element IDs");

        const string sse = "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"Olá \"}\n\n"
            + "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Revit\"}\n\n"
            + "data: {\"type\":\"response.completed\"}\n\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var chunks = new List<string>();
        var answer = await OpenAIClient.ReadSseAsync(stream, chunks.Add, default);
        Check(answer == "Olá Revit" && chunks.Count == 2, "SSE incremental delivery");
        try
        {
            using var broken = new MemoryStream(Encoding.UTF8.GetBytes("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n"));
            await OpenAIClient.ReadSseAsync(broken, _ => { }, default);
            throw new Exception("FAIL: truncated SSE accepted");
        }
        catch (IOException) { Check(true, "truncated SSE is not successful"); }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try
        {
            await OpenAIClient.ReadSseAsync(new MemoryStream(), _ => { }, cancellation.Token);
            throw new Exception("FAIL: cancellation");
        }
        catch (OperationCanceledException) { Check(true, "stream cancellation"); }
        using var handler = new FakeHandler(sse);
        using var http = new HttpClient(handler);
        var client = new OpenAIClient("test-key", http);
        await client.ChatAsync(new[] { new ChatMessage { Text = "Analise", Images = new() { new() { DataUrl = "data:image/png;base64,AA==" } } } },
            "context", "gpt-5", _ => { }, default);
        using var payload = JsonDocument.Parse(handler.Body!);
        Check(payload.RootElement.GetProperty("stream").GetBoolean() && !payload.RootElement.GetProperty("store").GetBoolean(), "stream=true and store=false");
        Check(payload.RootElement.GetProperty("input")[0].GetProperty("content")[1].GetProperty("type").GetString() == "input_image", "image payload");
        Check(payload.RootElement.GetProperty("reasoning").GetProperty("effort").GetString() == "low", "GPT-5 low-effort conversation");
        await client.ChatAsync(Array.Empty<ChatMessage>(), "", "other-model", _ => { }, default);
        using var otherPayload = JsonDocument.Parse(handler.Body!);
        Check(!otherPayload.RootElement.TryGetProperty("reasoning", out _), "no unsupported reasoning setting for other models");
        const string geminiSse = "data: {\"type\":\"step.delta\",\"delta\":{\"type\":\"text\",\"text\":\"Olá Gemini\"}}\n\n"
            + "data: {\"type\":\"interaction.completed\",\"interaction\":{\"status\":\"completed\"}}\n\n";
        using var geminiHandler = new FakeHandler(geminiSse);
        using var geminiHttp = new HttpClient(geminiHandler);
        var gemini = new GeminiClient("AIza-test-placeholder-key", geminiHttp);
        var geminiText = await gemini.ChatAsync(
            new[] { new ChatMessage { Text = "Analise", Images = new() { new() { DataUrl = "data:image/png;base64,AA==" } } } },
            "context", GeminiClient.DefaultModel, _ => { }, default);
        using var geminiPayload = JsonDocument.Parse(geminiHandler.Body!);
        Check(geminiText == "Olá Gemini", "Gemini SSE incremental delivery");
        Check(geminiPayload.RootElement.GetProperty("model").GetString() == GeminiClient.DefaultModel &&
            geminiPayload.RootElement.GetProperty("store").GetBoolean() == false, "Gemini model and store=false");
        Check(geminiPayload.RootElement.GetProperty("input")[1].GetProperty("type").GetString() == "image" &&
            geminiPayload.RootElement.GetProperty("input")[1].GetProperty("data").GetString() == "AA==" &&
            geminiHandler.ApiKeyHeader, "Gemini image and API-key header");
        const string geminiPlanResponse = "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":\"{\\\"summary\\\":\\\"ok\\\",\\\"notes\\\":[],\\\"actions\\\":[]}\"}]}]}";
        using var planHandler = new FakeHandler(geminiPlanResponse);
        using var planHttp = new HttpClient(planHandler);
        var planClient = new GeminiClient("AIza-test-placeholder-key", planHttp);
        var geminiPlan = await planClient.PlanAsync("teste", Array.Empty<ChatMessage>(), "context", Array.Empty<ImageAttachment>(), GeminiClient.DefaultModel, default);
        using var planPayload = JsonDocument.Parse(planHandler.Body!);
        Check(geminiPlan.Summary == "ok" && planPayload.RootElement.TryGetProperty("response_format", out _), "Gemini structured Revit plan");
        foreach (var sample in new[] {
            ("{\"type\":\"response.incomplete\",\"response\":{\"incomplete_details\":{\"reason\":\"max_output_tokens\"}}}", "limite de tokens", "token exhaustion diagnosed"),
            ("{\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"insufficient_quota\",\"message\":\"DO_NOT_ECHO\"}}}", "saldo", "stream quota failure diagnosed"),
            ("{\"type\":\"error\",\"code\":\"unknown\",\"message\":\"DO_NOT_ECHO\"}", "sem motivo reconhecido", "unknown service error sanitized") })
        {
            try {
                using var failed = new MemoryStream(Encoding.UTF8.GetBytes("data: " + sample.Item1 + "\n\n"));
                await OpenAIClient.ReadSseAsync(failed, _ => { }, default);
                throw new Exception("FAIL: service failure accepted");
            }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains(sample.Item2) && !ex.Message.Contains("DO_NOT_ECHO"), sample.Item3); }
        }
        handler.Status = HttpStatusCode.Unauthorized;
        try { await client.ChatAsync(Array.Empty<ChatMessage>(), "", "gpt-5", _ => { }, default); throw new Exception("FAIL: HTTP error"); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("Chave inválida"), "authentication error without secret/body leakage"); }
        Console.WriteLine($"ALL {passed} TESTS PASSED — no network requests.");
    }
    private sealed class FakeHandler(string response) : HttpMessageHandler
    {
        public string? Body;
        public bool ApiKeyHeader;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            ApiKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var values) && values.Any(v => v.StartsWith("AIza-", StringComparison.Ordinal));
            return new HttpResponseMessage(Status) { Content = new StringContent(response) };
        }
    }
}
