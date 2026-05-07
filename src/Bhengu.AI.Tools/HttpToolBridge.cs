using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Tools
{
    /// <summary>
    /// HTTP-backed implementation of <see cref="IToolBridge"/> that routes tool calls to the
    /// TheGeekNetwork APIs over REST. Tool-name -> endpoint mapping is provided for the
    /// representative operations defined in <see cref="TheGeekNetworkTools"/>; unmapped
    /// tools return a structured TODO error rather than throwing.
    ///
    /// No network calls happen during construction or via the <see cref="AvailableTools"/>
    /// property - only <see cref="InvokeAsync"/> hits the wire.
    /// </summary>
    public sealed class HttpToolBridge : IToolBridge
    {
        private readonly HttpClient _http;
        private readonly Uri _baseUri;
        private readonly IReadOnlyList<ToolDefinition> _tools;
        private readonly IReadOnlyDictionary<string, EndpointMapping> _routes;

        public HttpToolBridge(string baseUrl, HttpClient httpClient)
            : this(baseUrl, httpClient, TheGeekNetworkTools.GetAllTools())
        {
        }

        public HttpToolBridge(string baseUrl, HttpClient httpClient, IReadOnlyList<ToolDefinition> tools)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(tools);

            _http = httpClient;
            _baseUri = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute);
            _tools = tools;
            _routes = BuildRoutes();
        }

        public IReadOnlyList<ToolDefinition> AvailableTools => _tools;

        public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(invocation);

            if (!_routes.TryGetValue(invocation.ToolName, out var mapping))
            {
                return new ToolResult
                {
                    ToolName = invocation.ToolName,
                    Success = false,
                    Error = $"TODO: tool '{invocation.ToolName}' is declared but not yet routed to an endpoint."
                };
            }

            try
            {
                var url = ResolveUrl(mapping, invocation.Arguments);
                using var request = BuildRequest(mapping, url, invocation.Arguments);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

                object? body = null;
                if (response.Content is not null &&
                    response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
                {
                    body = await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
                }
                else if (response.Content is not null)
                {
                    body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new ToolResult
                    {
                        ToolName = invocation.ToolName,
                        Success = false,
                        Result = body,
                        Error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                    };
                }

                return new ToolResult
                {
                    ToolName = invocation.ToolName,
                    Success = true,
                    Result = body
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    ToolName = invocation.ToolName,
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        // ------------------------------------------------------------------
        // Internal: routing table
        // ------------------------------------------------------------------

        private sealed record EndpointMapping(HttpMethod Method, string PathTemplate, string Body);

        // Body strategy values: "none", "query", "json".
        private const string BodyNone = "none";
        private const string BodyQuery = "query";
        private const string BodyJson = "json";

        private static IReadOnlyDictionary<string, EndpointMapping> BuildRoutes() =>
            new Dictionary<string, EndpointMapping>(StringComparer.Ordinal)
            {
                // Account
                ["tgn.account.get_profile"] = new(HttpMethod.Get, "account/v1/users/{user_id}", BodyNone),
                ["tgn.account.update_profile"] = new(HttpMethod.Patch, "account/v1/users/me", BodyJson),

                // Audit
                ["tgn.audit.list_events"] = new(HttpMethod.Get, "audit/v1/events", BodyQuery),

                // Auth
                ["tgn.auth.request_otp"] = new(HttpMethod.Post, "auth/v1/otp/request", BodyJson),
                ["tgn.auth.verify_otp"] = new(HttpMethod.Post, "auth/v1/otp/verify", BodyJson),
                ["tgn.auth.push_to_app"] = new(HttpMethod.Post, "auth/v1/push-to-app", BodyJson),

                // BidBaas
                ["tgn.bidbaas.list_active_auctions"] = new(HttpMethod.Get, "bidbaas/v1/auctions/active", BodyQuery),
                ["tgn.bidbaas.place_bid"] = new(HttpMethod.Post, "bidbaas/v1/auctions/{auction_id}/bids", BodyJson),
                ["tgn.bidbaas.get_auction_details"] = new(HttpMethod.Get, "bidbaas/v1/auctions/{auction_id}", BodyNone),

                // BillPayment
                ["tgn.billpayment.list_billers"] = new(HttpMethod.Get, "billpayment/v1/billers", BodyQuery),
                ["tgn.billpayment.pay_bill"] = new(HttpMethod.Post, "billpayment/v1/payments", BodyJson),

                // Blockchain
                ["tgn.blockchain.get_transaction"] = new(HttpMethod.Get, "blockchain/v1/transactions/{tx_hash}", BodyNone),
                ["tgn.blockchain.get_address_info"] = new(HttpMethod.Get, "blockchain/v1/addresses/{address}", BodyNone),

                // Butler
                ["tgn.butler.log_interaction"] = new(HttpMethod.Post, "butler/v1/interactions", BodyJson),
                ["tgn.butler.get_user_context"] = new(HttpMethod.Get, "butler/v1/users/{user_id}/context", BodyNone),

                // CircleAether
                ["tgn.circleaether.get_node_status"] = new(HttpMethod.Get, "circleaether/v1/nodes/{device_id}/status", BodyNone),
                ["tgn.circleaether.list_nearby_peers"] = new(HttpMethod.Get, "circleaether/v1/peers/nearby", BodyQuery),

                // Ecommerce
                ["tgn.ecommerce.search_products"] = new(HttpMethod.Get, "ecommerce/v1/products/search", BodyQuery),
                ["tgn.ecommerce.get_product"] = new(HttpMethod.Get, "ecommerce/v1/products/{product_id}", BodyNone),

                // Electricity
                ["tgn.electricity.buy_token"] = new(HttpMethod.Post, "electricity/v1/tokens", BodyJson),
                ["tgn.electricity.list_recent_purchases"] = new(HttpMethod.Get, "electricity/v1/purchases", BodyQuery),

                // Geo
                ["tgn.geo.get_user_location"] = new(HttpMethod.Get, "geo/v1/users/me/location", BodyNone),
                ["tgn.geo.geocode_address"] = new(HttpMethod.Get, "geo/v1/geocode", BodyQuery),

                // Glocell
                ["tgn.glocell.list_products"] = new(HttpMethod.Get, "glocell/v1/products", BodyQuery),

                // Incentives
                ["tgn.incentives.get_qi_balance"] = new(HttpMethod.Get, "incentives/v1/qi/balance", BodyNone),
                ["tgn.incentives.list_active_quests"] = new(HttpMethod.Get, "incentives/v1/quests/active", BodyQuery),

                // KiffStore
                ["tgn.kiffstore.search_items"] = new(HttpMethod.Get, "kiffstore/v1/items/search", BodyQuery),

                // Ledger
                ["tgn.ledger.get_account_balance"] = new(HttpMethod.Get, "ledger/v1/accounts/{account_id}/balance", BodyNone),
                ["tgn.ledger.list_entries"] = new(HttpMethod.Get, "ledger/v1/accounts/{account_id}/entries", BodyQuery),

                // Localization
                ["tgn.localization.translate_text"] = new(HttpMethod.Post, "localization/v1/translate", BodyJson),
                ["tgn.localization.list_supported_languages"] = new(HttpMethod.Get, "localization/v1/languages", BodyNone),

                // Maps
                ["tgn.maps.geocode"] = new(HttpMethod.Get, "maps/v1/geocode", BodyQuery),
                ["tgn.maps.reverse_geocode"] = new(HttpMethod.Get, "maps/v1/reverse-geocode", BodyQuery),

                // MapsData
                ["tgn.mapsdata.search_pois"] = new(HttpMethod.Get, "mapsdata/v1/pois/search", BodyQuery),

                // Media
                ["tgn.media.create_upload_url"] = new(HttpMethod.Post, "media/v1/uploads", BodyJson),
                ["tgn.media.get_media"] = new(HttpMethod.Get, "media/v1/media/{media_id}", BodyNone),

                // Messaging
                ["tgn.messaging.send_message"] = new(HttpMethod.Post, "messaging/v1/messages", BodyJson),
                ["tgn.messaging.list_conversations"] = new(HttpMethod.Get, "messaging/v1/conversations", BodyQuery),
                ["tgn.messaging.get_messages"] = new(HttpMethod.Get, "messaging/v1/conversations/{conversation_id}/messages", BodyQuery),

                // Notification
                ["tgn.notification.send_push"] = new(HttpMethod.Post, "notification/v1/push", BodyJson),
                ["tgn.notification.list_for_user"] = new(HttpMethod.Get, "notification/v1/notifications", BodyQuery),

                // OpSupport
                ["tgn.opsupport.create_ticket"] = new(HttpMethod.Post, "opsupport/v1/tickets", BodyJson),
                ["tgn.opsupport.get_system_status"] = new(HttpMethod.Get, "opsupport/v1/status", BodyNone),

                // Panik
                ["tgn.panik.trigger_sos"] = new(HttpMethod.Post, "panik/v1/alerts", BodyJson),
                ["tgn.panik.cancel_sos"] = new(HttpMethod.Post, "panik/v1/alerts/{alert_id}/cancel", BodyJson),

                // Payfast
                ["tgn.payfast.create_payment"] = new(HttpMethod.Post, "payfast/v1/payments", BodyJson),

                // Sdpkt
                ["tgn.sdpkt.get_balance"] = new(HttpMethod.Get, "sdpkt/v1/wallet/balance", BodyNone),
                ["tgn.sdpkt.send_payment"] = new(HttpMethod.Post, "sdpkt/v1/wallet/transfers", BodyJson),
                ["tgn.sdpkt.get_transactions"] = new(HttpMethod.Get, "sdpkt/v1/wallet/transactions", BodyQuery),

                // ShhMoney
                ["tgn.shhmoney.create_discreet_payment"] = new(HttpMethod.Post, "shhmoney/v1/payments", BodyJson),

                // SleptOn
                ["tgn.slepton.list_stories"] = new(HttpMethod.Get, "slepton/v1/stories", BodyQuery),
                ["tgn.slepton.get_story"] = new(HttpMethod.Get, "slepton/v1/stories/{story_id}", BodyNone),

                // SortedClothing
                ["tgn.sortedclothing.search_items"] = new(HttpMethod.Get, "sortedclothing/v1/items/search", BodyQuery),

                // TagMe
                ["tgn.tagme.create_tag"] = new(HttpMethod.Post, "tagme/v1/tags", BodyJson),
                ["tgn.tagme.list_nearby_tags"] = new(HttpMethod.Get, "tagme/v1/tags/nearby", BodyQuery),

                // Takemehome
                ["tgn.takemehome.search_flights"] = new(HttpMethod.Get, "takemehome/v1/flights/search", BodyQuery),
                ["tgn.takemehome.search_stays"] = new(HttpMethod.Get, "takemehome/v1/stays/search", BodyQuery),

                // TheHotList
                ["tgn.thehotlist.list_entries"] = new(HttpMethod.Get, "thehotlist/v1/entries", BodyQuery),

                // TheJobCenter
                ["tgn.thejobcenter.search_jobs"] = new(HttpMethod.Get, "thejobcenter/v1/jobs/search", BodyQuery),
                ["tgn.thejobcenter.apply"] = new(HttpMethod.Post, "thejobcenter/v1/jobs/{job_id}/applications", BodyJson),

                // ThirdParty
                ["tgn.thirdparty.list_integrations"] = new(HttpMethod.Get, "thirdparty/v1/integrations", BodyNone),
                ["tgn.thirdparty.invoke_integration"] = new(HttpMethod.Post, "thirdparty/v1/integrations/{integration_name}/invoke", BodyJson),

                // TrustSeal
                ["tgn.trustseal.get_status"] = new(HttpMethod.Get, "trustseal/v1/status", BodyNone),
                ["tgn.trustseal.start_verification"] = new(HttpMethod.Post, "trustseal/v1/verifications", BodyJson),

                // Wallet
                ["tgn.wallet.get_balance"] = new(HttpMethod.Get, "wallet/v1/balance", BodyQuery),
                ["tgn.wallet.get_transactions"] = new(HttpMethod.Get, "wallet/v1/transactions", BodyQuery),

                // WhatWeWant
                ["tgn.whatwewant.list_stories"] = new(HttpMethod.Get, "whatwewant/v1/stories", BodyQuery),
                ["tgn.whatwewant.get_story"] = new(HttpMethod.Get, "whatwewant/v1/stories/{story_id}", BodyNone),

                // Wolverine
                ["tgn.wolverine.list_jobs"] = new(HttpMethod.Get, "wolverine/v1/jobs", BodyQuery)
            };

        // ------------------------------------------------------------------
        // Internal: URL / request building
        // ------------------------------------------------------------------

        private Uri ResolveUrl(EndpointMapping mapping, IReadOnlyDictionary<string, object?> arguments)
        {
            // Substitute {placeholder} segments using arguments. Substituted args are NOT
            // duplicated into the body/query - we strip them out via BuildBodyArgs below.
            var path = mapping.PathTemplate;
            foreach (var placeholder in ExtractPlaceholders(mapping.PathTemplate))
            {
                if (!arguments.TryGetValue(placeholder, out var raw) || raw is null)
                {
                    throw new InvalidOperationException(
                        $"Tool argument '{placeholder}' is required to build URL '{mapping.PathTemplate}'.");
                }
                path = path.Replace("{" + placeholder + "}", Uri.EscapeDataString(raw.ToString() ?? string.Empty), StringComparison.Ordinal);
            }

            var url = new Uri(_baseUri, path);

            if (mapping.Body == BodyQuery)
            {
                var query = BuildQueryString(BuildBodyArgs(mapping, arguments));
                if (!string.IsNullOrEmpty(query))
                {
                    var ub = new UriBuilder(url) { Query = query };
                    url = ub.Uri;
                }
            }

            return url;
        }

        private static HttpRequestMessage BuildRequest(
            EndpointMapping mapping,
            Uri url,
            IReadOnlyDictionary<string, object?> arguments)
        {
            var request = new HttpRequestMessage(mapping.Method, url);
            if (mapping.Body == BodyJson)
            {
                var body = BuildBodyArgs(mapping, arguments);
                request.Content = JsonContent.Create(body);
            }
            return request;
        }

        private static Dictionary<string, object?> BuildBodyArgs(
            EndpointMapping mapping,
            IReadOnlyDictionary<string, object?> arguments)
        {
            // Drop placeholders from the body/query - they're already in the URL.
            var placeholders = new HashSet<string>(ExtractPlaceholders(mapping.PathTemplate), StringComparer.Ordinal);
            var result = new Dictionary<string, object?>(arguments.Count, StringComparer.Ordinal);
            foreach (var kvp in arguments)
            {
                if (placeholders.Contains(kvp.Key)) continue;
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }

        private static IEnumerable<string> ExtractPlaceholders(string template)
        {
            var i = 0;
            while (i < template.Length)
            {
                var open = template.IndexOf('{', i);
                if (open < 0) yield break;
                var close = template.IndexOf('}', open + 1);
                if (close < 0) yield break;
                yield return template.Substring(open + 1, close - open - 1);
                i = close + 1;
            }
        }

        private static string BuildQueryString(IReadOnlyDictionary<string, object?> args)
        {
            if (args.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder();
            var first = true;
            foreach (var kvp in args)
            {
                if (kvp.Value is null) continue;
                var rendered = RenderQueryValue(kvp.Value);
                if (rendered is null) continue;

                if (!first) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kvp.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(rendered));
                first = false;
            }
            return sb.ToString();
        }

        private static string? RenderQueryValue(object value) =>
            value switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
    }
}
