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

        var networkTimeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 30, 900));

        _httpClient = new HttpClient
        {
            // Let ClientPipelineOptions.NetworkTimeout govern per-request timeouts cleanly.
            Timeout = Timeout.InfiniteTimeSpan
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
            NetworkTimeout = networkTimeout,
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
                Sana verilen git diff'inde `+` ile eklenen HER satırı tek tek, titizlikle incele. Aşağıdaki tüm kategorileri eksiksiz denetle:

                1. **Code Smells & Dead Code (Ölü Kod / Kalıntılar)**:
                   - Kullanılmayan değişkenler, parametreler, field'lar veya `using` ifadeleri
                   - Geçici / anlamsız / test amaçlı unutulmuş değişken ve değerler (ör. `TestVar123 = "abc"`, `temp`, `asdf`, `foo`, debug amaçlı `Console.WriteLine`)
                   - Yorum satırına alınmış ölü kod blokları, erişilemeyen (unreachable) kodlar
                   - Magic numbers / magic strings (anlamlı sabit/enum yerine ham değerler)
                   - C#/.NET adlandırma kurallarına aykırı isimler (ör. yerel değişkenlerde `camelCase` yerine `PascalCase`, alt çizgi/rakam karmaşası, anlamsız kısaltmalar)

                2. **Technical Debt (Teknik Borç)**:
                   - Kod tekrarları (DRY ihlalleri), kopyala-yapıştır bloklar
                   - Aşırı uzun metotlar (Long Method), şişkin sınıflar (God Class), derin iç içe `if/for` blokları (yüksek siklomatik karmaşıklık)
                   - Sabit kodlanmış (hardcoded) konfigürasyon / URL / dosya yolu / kültür-bağımlı değerler
                   - `TODO` / `HACK` / `FIXME` borçları ve test edilebilirliği düşüren yapılar

                3. **SOLID Prensipleri**:
                   - **SRP**: Birden fazla sorumluluk üstlenen sınıf veya metotlar
                   - **OCP**: Yeni senaryo için mevcut `if/else` veya `switch` bloklarını sürekli değiştirmeyi gerektiren yapılar
                   - **LSP**: Alt sınıfın üst sınıf davranış sözleşmesini bozması
                   - **ISP**: Gereksiz büyük / kullanılmayan metotlar barındıran arayüzler
                   - **DIP**: `new ConcreteService()` gibi doğrudan somut sınıfa bağımlılık ve Dependency Injection eksikleri

                4. **Design Patterns & Mimari**:
                   - Uygun tasarım deseniyle (Strategy, Factory, Options, Builder, Repository, Guard Clauses vb.) sadeleştirilebilecek karmaşık akışlar
                   - Yanlış/aşırı pattern kullanımı (over-engineering, Service Locator veya gereksiz Singleton gibi anti-pattern'ler)

                5. **Performans, Async & Kaynak Yönetimi**:
                   - Async/await yanlış kullanımı (`.Result`, `.Wait()`, `async void`, `CancellationToken` aktarılmaması)
                   - Kaynak yönetimi (`using` / `IDisposable` eksikleri, `HttpClient`/connection/stream sızıntıları)
                   - Hata yönetimi (boş `catch` blokları, genel `Exception` yutma), null güvenliği ve edge case'ler

                Yanıtını temiz Türkçe Markdown olarak ver. Her bulgu için mutlaka şunları belirt:
                - **Kategori** (`Code Smell` / `Technical Debt` / `SOLID` / `Design Pattern` / `Naming` / `Performance & Reliability`)
                - **Önem Derecesi** (`HIGH` / `MEDIUM` / `LOW`)
                - **Dosya & Kod Parçası** (diff'teki birebir kodu alıntıla)
                - **Sorun Açıklaması**
                - **Önerilen İyileştirme** (somut kısa kod önerisiyle)

                Önemli: Küçük görünen ama gerçek olan code smell'leri, kullanılmayan/geçici değişkenleri ve adlandırma ihlallerini ASLA atlama. Güvenlik konularını tekrar etmekten kaçın, kalite ve mimariye odaklan.
                """,
            name: "CodeQualityReviewerAgent",
            description: "Git diff üzerinde code smell, technical debt, SOLID, design pattern, performans ve bakım kolaylığı analizi yapan uzman ajan.");
    }

    public AIAgent CreateLeadReviewerAgent()
    {
        return _chatClient.AsAIAgent(
            instructions: """
                Sen ekibin Lead Code Reviewer ajanısın.
                Görevin:
                1. SecurityReviewerAgent ve CodeQualityReviewerAgent tarafından üretilen code review bulgularını orijinal git diff ile karşılaştırarak denetlemek.
                2. Yalnızca gerçek halüsinasyonları (diff'te hiç olmayan kodlar), açık yanlış pozitifleri (false positive) veya birebir tekrar eden maddeleri elemek.
                3. Diff'te gerçekten bulunan **code smell, kullanılmayan/geçici değişkenler, adlandırma ihlalleri, teknik borç (technical debt), SOLID ve design pattern** bulgularını "küçük/önemsiz" diyerek ASLA eleme — uygun önem derecesiyle (`HIGH`/`MEDIUM`/`LOW`) nihai raporda koru.
                4. Review sonuçları güvenilir, doğru ve PR'a yorum olarak atılmaya hazırsa ONAYLAMAK (`approved: true`), aksi durumda reddetmek (`approved: false`).
                5. Onayladığında, GitHub Pull Request yorumu olarak doğrudan yayınlanabilecek profesyonel, net ve eyleme geçirilebilir tek bir Türkçe Markdown yorumu hazırlamak. Yorumda bulguları net başlıklar altında grupla:
                   - `### 🔒 Güvenlik Bulguları` (varsa)
                   - `### 🧹 Code Smell, Naming & Dead Code` (varsa)
                   - `### 🏗️ SOLID, Design Patterns & Technical Debt` (varsa)
                   - `### ⚡ Performans, Async & Hata Yönetimi` (varsa)
                   - `### ✅ Özet`

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
