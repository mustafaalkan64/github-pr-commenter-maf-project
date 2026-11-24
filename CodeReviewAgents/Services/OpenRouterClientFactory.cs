using System.ClientModel;
using System.ClientModel.Primitives;
using CodeReviewAgents.Configuration;
using Microsoft.Agents.AI;
using OpenAI;
using OpenAI.Chat;

namespace CodeReviewAgents.Services;

public sealed class OpenRouterClientFactory : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ChatClient _chatClient;

    public OpenRouterClientFactory(OpenRouterSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "OpenRouter:ApiKey tanımlı değil. Lütfen 'OPENROUTER_API_KEY' ortam değişkeni veya 'dotnet user-secrets set OpenRouter:ApiKey <key>' ile tanımlayın.");
        }

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };

        if (!string.IsNullOrWhiteSpace(settings.SiteUrl))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", settings.SiteUrl);
        }

        if (!string.IsNullOrWhiteSpace(settings.AppName))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", settings.AppName);
        }

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(settings.BaseUrl),
            Transport = new HttpClientPipelineTransport(_httpClient)
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential(settings.ApiKey), options);
        _chatClient = openAiClient.GetChatClient(settings.Model);
    }

    public AIAgent CreateSecurityAgent()
    {
        return _chatClient.AsAIAgent(
            instructions: """
                Sen kıdemli bir Uygulama Güvenliği (AppSec) Code Review ajanısın.
                Sana verilen git diff'ini yalnızca güvenlik açısından derinlemesine incele:
                - SQL Injection, Command Injection, Path Traversal, XSS, SSRF, Deserialization açıkları
                - Hardcoded secret / API key / parola sızıntıları
                - Kimlik doğrulama / yetkilendirme eksikleri ve hassas veri ifşası (PasswordHash, PII loglama vb.)
                - Güvensiz kriptografi veya eksik girdi doğrulaması

                Yanıtını temiz Türkçe Markdown olarak ver. Her bulgu için:
                - **Önem Derecesi** (`CRITICAL` / `HIGH` / `MEDIUM` / `LOW`)
                - **Dosya & Satır / Kod Parçası**
                - **Risk Açıklaması**
                - **Önerilen Düzeltme**
                Eğer kritik güvenlik bulgusu yoksa bunu açıkça belirt.
                """,
            name: "SecurityReviewerAgent",
            description: "Git diff üzerinde güvenlik açığı analizi yapan uzman ajan.");
    }

    public AIAgent CreateCodeQualityAgent()
    {
        return _chatClient.AsAIAgent(
            instructions: """
                Sen kıdemli bir Yazılım Mimarisi ve Code Quality Review ajanısın.
                Sana verilen git diff'ini kod kalitesi ve sürdürülebilirlik açısından incele:
                - Async/await yanlış kullanımı, senkron I/O bloklamaları
                - Kaynak yönetimi (`using` / `IDisposable` eksikleri, connection/reader sızıntıları)
                - Hata yönetimi, null güvenliği, edge case'ler
                - SOLID prensipleri, okunabilirlik, test edilebilirlik ve performans iyileştirmeleri

                Yanıtını temiz Türkçe Markdown olarak ver. Her bulgu için:
                - **Önem Derecesi** (`HIGH` / `MEDIUM` / `LOW`)
                - **Dosya & Satır / Kod Parçası**
                - **Sorun Açıklaması**
                - **Önerilen İyileştirme**
                Güvenlik konularını tekrar etmekten kaçın, kalite ve mimariye odaklan.
                """,
            name: "CodeQualityReviewerAgent",
            description: "Git diff üzerinde kod kalitesi, performans ve bakım kolaylığı analizi yapan uzman ajan.");
    }

    public AIAgent CreateLeadReviewerAgent()
    {
        return _chatClient.AsAIAgent(
            instructions: """
                Sen ekibin Lead Code Reviewer ajanısın.
                Görevin:
                1. SecurityReviewerAgent ve CodeQualityReviewerAgent tarafından üretilen code review bulgularını orijinal git diff ile karşılaştırarak denetlemek.
                2. Halüsinasyon, yanlış pozitif (false positive) veya gereksiz/tekrar eden maddeleri elemek.
                3. Review sonuçları güvenilir, doğru ve PR'a yorum olarak atılmaya hazırsa ONAYLAMAK (`approved: true`), aksi durumda reddetmek (`approved: false`).
                4. Onayladığında, GitHub Pull Request yorumu olarak doğrudan yayınlanabilecek profesyonel, net ve eyleme geçirilebilir tek bir Türkçe Markdown yorumu hazırlamak.

                YANITINI KESİNLİKLE AŞAĞIDAKİ JSON FORMATINDA VER (başka hiçbir metin ekleme):
                ```json
                {
                  "approved": true,
                  "decisionReason": "Onay veya red gerekçesi (kısa özet)",
                  "auditSummary": "İki ajanın bulgularının denetim özeti (hangi bulgular doğrulandı, hangileri elendi)",
                  "finalPrCommentMarkdown": "## 🤖 AI Code Review (Lead Agent Onaylı)\n\n..."
                }
                ```
                """,
            name: "LeadReviewerAgent",
            description: "Security ve Code Quality review bulgularını denetleyip onaylayan ve nihai PR yorumunu oluşturan Lead ajan.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
