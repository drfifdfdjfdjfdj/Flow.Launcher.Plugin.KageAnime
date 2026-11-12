using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.SharedCommands;

namespace KageAnime
{
    /// <summary>
    /// KageAnime — поиск аниме и открытие плеера на anime.hitler.site.
    /// Кеш в temp, постеры кешируются локально. API: /api/search, /api/ongoing.
    /// </summary>
    public class KageAnime : IAsyncPlugin, IContextMenu
    {
        private const string ApiBase = "https://anime.hitler.site";
        private const string AppUrl = "https://anime.hitler.site/";

        private PluginInitContext _context;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private readonly Dictionary<string, (DateTime at, string json)> _cache = new();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

        public async Task InitAsync(PluginInitContext context)
        {
            _context = context;
            Directory.CreateDirectory(CacheDir());
            await Task.CompletedTask;
        }

        private static string CacheDir()
        {
            return Path.Combine(Path.GetTempPath(), "kageanime_flow");
        }

        private async Task<string> GetJsonCachedAsync(string url, System.Threading.CancellationToken token)
        {
            var key = "url:" + url;
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.at < CacheTtl)
                    return hit.json;
            }
            var json = await Http.GetStringAsync(url, token).ConfigureAwait(false);
            lock (_cache)
            {
                _cache[key] = (DateTime.UtcNow, json);
            }
            return json;
        }

        private static JsonElement Parse(JsonElement root, string prop)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out var v))
                return v;
            return default;
        }

        private async Task<List<JsonElement>> SearchAsync(string query, int limit, System.Threading.CancellationToken token)
        {
            var url = $"{ApiBase}/api/search?q={Uri.EscapeDataString(query)}&limit={limit}";
            var json = await GetJsonCachedAsync(url, token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var items = new List<JsonElement>();
            foreach (var prop in new[] { "items", "results" })
            {
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in arr.EnumerateArray())
                        items.Add(it.Clone());
                    break;
                }
            }
            // Точное совпадение названия — наверх
            items.Sort((a, b) =>
            {
                int sa = ExactMatch(a, query), sb = ExactMatch(b, query);
                return sb.CompareTo(sa);
            });
            return items;
        }

        private static int ExactMatch(JsonElement item, string query)
        {
            var q = query.Trim().ToLowerInvariant();
            foreach (var prop in new[] { "title", "alt_title" })
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(prop, out var v) &&
                    v.ValueKind == JsonValueKind.String && v.GetString().ToLowerInvariant() == q)
                    return 1;
            }
            return 0;
        }

        private async Task<List<JsonElement>> OngoingAsync(int limit, System.Threading.CancellationToken token)
        {
            var url = $"{ApiBase}/api/ongoing?source=kodik&limit={limit}";
            var json = await GetJsonCachedAsync(url, token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var items = new List<JsonElement>();
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in arr.EnumerateArray())
                    items.Add(it.Clone());
            }
            return items;
        }

        private static string Str(JsonElement item, string prop)
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(prop, out var v))
            {
                if (v.ValueKind == JsonValueKind.String) return v.GetString();
                if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            }
            return "";
        }

        private static string SubTitle(JsonElement it)
        {
            var parts = new List<string>();
            var alt = Str(it, "alt_title");
            if (!string.IsNullOrWhiteSpace(alt)) parts.Add(alt);
            var year = Str(it, "year");
            if (!string.IsNullOrWhiteSpace(year) && year != "0") parts.Add(year);
            var score = Str(it, "score");
            if (double.TryParse(score, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var sv) && sv > 0)
                parts.Add("★ " + sv.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            var kind = Str(it, "kind");
            if (!string.IsNullOrWhiteSpace(kind))
            {
                var ru = kind switch
                {
                    "movie" => "фильм",
                    "tv" => "сериал",
                    "ona" => "ONA",
                    "ova" => "OVA",
                    "special" => "спешл",
                    _ => kind
                };
                parts.Add(ru);
            }
            var src = Str(it, "source_name");
            if (!string.IsNullOrWhiteSpace(src)) parts.Add(src);
            return string.Join("  ·  ", parts);
        }

        private static string ThumbOf(JsonElement it)
        {
            var t = Str(it, "thumbnail");
            if (string.IsNullOrWhiteSpace(t)) t = Str(it, "image");
            return t;
        }

        private static string PosterPath(string thumb)
        {
            var dir = CacheDir();
            // Расширение из URL — Flow/WPF не всегда декодирует файлы без известного расширения (.img давал пустые иконки)
            var ext = ".jpg";
            try
            {
                var lp = new Uri(thumb).AbsolutePath.ToLowerInvariant();
                if (lp.EndsWith(".png")) ext = ".png";
                else if (lp.EndsWith(".webp")) ext = ".webp";
                else if (lp.EndsWith(".gif")) ext = ".gif";
                else if (lp.EndsWith(".jpeg")) ext = ".jpeg";
            }
            catch { }
            var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(thumb))).Substring(0, 32) + ext;
            return Path.Combine(dir, name);
        }

        private static async Task PrefetchPosterAsync(string thumb, System.Threading.CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(thumb)) return;
            var path = PosterPath(thumb);
            if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(7))
                return;
            try
            {
                var bytes = await Http.GetByteArrayAsync(thumb, token).ConfigureAwait(false);
                File.WriteAllBytes(path, bytes);
            }
            catch { }
        }

        private string Icon(JsonElement it)
        {
            var thumb = ThumbOf(it);
            if (string.IsNullOrWhiteSpace(thumb)) return "icon.png";
            var path = PosterPath(thumb);
            if (File.Exists(path))
                return path;
            // Синхронный добор на случай, если префетч не успел (редко)
            try
            {
                var bytes = Http.GetByteArrayAsync(thumb).ConfigureAwait(false).GetAwaiter().GetResult();
                File.WriteAllBytes(path, bytes);
                return path;
            }
            catch
            {
                return "icon.png";
            }
        }

        private static string SiteLink(string id)
        {
            // Портал (anime.hitler.site) принимает deep-link через хеш: #shiki:12345 / #anime-12345
            if (string.IsNullOrWhiteSpace(id)) return AppUrl;
            var sid = id.Trim();
            if (sid.StartsWith("shiki:", StringComparison.OrdinalIgnoreCase)) return $"{AppUrl}#{sid}";
            return $"{AppUrl}#anime-{sid}";
        }

        private Result MakeAnimeResult(JsonElement it, int score)
        {
            var id = Str(it, "id");
            var title = Str(it, "title");
            var deepLink = SiteLink(id);
            return new Result
            {
                Title = title,
                SubTitle = SubTitle(it),
                IcoPath = Icon(it),
                Score = score,
                Action = c =>
                {
                    deepLink.OpenInBrowserTab();
                    return true;
                },
                ContextData = new List<object> { id, title }
            };
        }

        public async Task<List<Result>> QueryAsync(Query query, System.Threading.CancellationToken token)
        {            var q = (query.Search ?? "").Trim();
            var results = new List<Result>();

            if (string.IsNullOrEmpty(q))
            {
                results.Add(new Result
                {
                    Title = "KageAnime — печатай название аниме",
                    SubTitle = "Например: ka Наруто · ka ongoing · ka random",
                    IcoPath = "icon.png",
                    Score = 100,
                    Action = c => { AppUrl.OpenInBrowserTab(); return true; }
                });
                return results;
            }

            try
            {
                if (q.Equals("ongoing", StringComparison.OrdinalIgnoreCase))
                {
                    var ongoing = await OngoingAsync(9, token).ConfigureAwait(false);
                    // Постеры — параллельно, а не по одному
                    await Task.WhenAll(ongoing.Select(it => PrefetchPosterAsync(ThumbOf(it), token))).ConfigureAwait(false);
                    var i = 0;
                    foreach (var it in ongoing)
                        results.Add(MakeAnimeResult(it, 100 - i++));
                    results.Add(new Result
                    {
                        Title = "Открыть все онгоинги на KageAnime",
                        SubTitle = AppUrl,
                        IcoPath = "icon.png",
                        Score = 10,
                        Action = c => { AppUrl.OpenInBrowserTab(); return true; }
                    });
                    return results;
                }

                if (q.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var json = await Http.GetStringAsync($"{ApiBase}/api/anime/random", token).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        results.Add(MakeAnimeResult(doc.RootElement.Clone(), 100));
                    }
                    catch
                    {
                        results.Add(new Result
                        {
                            Title = "Не удалось получить случайное аниме",
                            SubTitle = "Открыть KageAnime",
                            IcoPath = "icon.png",
                            Score = 100,
                            Action = c => { AppUrl.OpenInBrowserTab(); return true; }
                        });
                    }
                    return results;
                }

                // Что ввел — первым пунктом, сразу видно запрос
                results.Add(new Result
                {
                    Title = $"🔍 «{q}»",
                    SubTitle = "Открыть поиск на KageAnime",
                    IcoPath = "icon.png",
                    Score = 1000,
                    Action = c => { AppUrl.OpenInBrowserTab(); return true; }
                });

                var found = await SearchAsync(q, 9, token).ConfigureAwait(false);
                // Постеры — параллельно, а не по одному
                await Task.WhenAll(found.Select(it => PrefetchPosterAsync(ThumbOf(it), token))).ConfigureAwait(false);
                var idx = 0;
                foreach (var it in found)
                    results.Add(MakeAnimeResult(it, 100 - idx++));

                if (results.Count == 1)
                {
                    results.Add(new Result
                    {
                        Title = "Ничего не найдено: " + q,
                        SubTitle = "Попробуй другое название или латиницу",
                        IcoPath = "icon.png",
                        Score = 100
                    });
                    return results;
                }
            }
            catch (Exception e)
            {
                results.Add(new Result
                {
                    Title = "KageAnime недоступен",
                    SubTitle = e.Message.Length > 80 ? e.Message.Substring(0, 80) : e.Message,
                    IcoPath = "icon.png",
                    Score = 100
                });
            }
            return results;
        }

        public List<Result> LoadContextMenus(Result selectedResult)
        {
            // Shift+Enter по тайтлу: вспомогательные действия
            var out_ = new List<Result>();
            string animeId = "", title = "";
            try
            {
                if (selectedResult.ContextData is List<object> ctx)
                {
                    if (ctx.Count > 0) animeId = ctx[0]?.ToString() ?? "";
                    if (ctx.Count > 1) title = ctx[1]?.ToString() ?? "";
                }
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(title))
            {
                out_.Add(new Result
                {
                    Title = $"Искать «{title}» на сайте",
                    SubTitle = "Страница поиска KageAnime",
                    IcoPath = "icon.png",
                    Action = c => { AppUrl.OpenInBrowserTab(); return true; }
                });
            }
            if (!string.IsNullOrWhiteSpace(animeId))
            {
                out_.Add(new Result
                {
                    Title = "Открыть тайтл в плеере",
                    SubTitle = "Прямая ссылка на webapp",
                    IcoPath = "icon.png",
                    Action = c => { SiteLink(animeId).OpenInBrowserTab(); return true; }
                });
            }
            out_.Add(new Result
            {
                Title = "Открыть KageAnime",
                SubTitle = AppUrl,
                IcoPath = "icon.png",
                Action = c => { AppUrl.OpenInBrowserTab(); return true; }
            });
            return out_;
        }
    }
}
