using System.Collections.Generic;

namespace Bhengu.AI.Tools
{
    /// <summary>
    /// Static catalogue of tool definitions covering the 36 APIs in TheGeekNetwork ecosystem.
    /// Tool names follow the pattern "tgn.&lt;api_slug&gt;.&lt;verb&gt;" in lowercase snake_case.
    /// Each API exposes 1-3 representative operations rather than every endpoint.
    /// </summary>
    public static class TheGeekNetworkTools
    {
        // ============================================================================
        // Helper for terse parameter construction.
        // ============================================================================

        private static ToolParameter Param(string type, string description, string[]? @enum = null) =>
            new() { Type = type, Description = description, Enum = @enum };

        // ============================================================================
        // AccountAPI — user accounts
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Account() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.account.get_profile",
                Description = "Get the authenticated user's account profile (display name, email, phone, country, KYC level).",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["user_id"] = Param("string", "Target user ID. Use 'me' for the current authenticated user.")
                },
                RequiredParameters = new[] { "user_id" }
            },
            new ToolDefinition
            {
                Name = "tgn.account.update_profile",
                Description = "Update profile fields for the current user (display name, avatar, country).",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["display_name"] = Param("string", "New display name. Optional."),
                    ["avatar_url"] = Param("string", "URL of the new avatar image. Optional."),
                    ["country_code"] = Param("string", "ISO-3166 alpha-2 country code. Optional.")
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // AuditAPI — audit trail
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Audit() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.audit.list_events",
                Description = "List recent audit events for the authenticated user, optionally filtered by category.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["category"] = Param("string", "Optional event category filter (e.g. 'auth', 'payment', 'profile')."),
                    ["limit"] = Param("number", "Max number of events to return. Default 50, max 500.")
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // AuthAPI — authentication / OTP / biometrics
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Auth() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.auth.request_otp",
                Description = "Send a one-time password to the user's phone via SMS for login or sensitive action confirmation.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["phone_number"] = Param("string", "E.164-formatted phone number, e.g. +27821234567."),
                    ["purpose"] = Param("string", "Reason for the OTP.", new[] { "login", "signup", "transaction", "reset_pin" })
                },
                RequiredParameters = new[] { "phone_number", "purpose" }
            },
            new ToolDefinition
            {
                Name = "tgn.auth.verify_otp",
                Description = "Verify an OTP code previously sent to the user. Returns a session token on success.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["phone_number"] = Param("string", "E.164-formatted phone number."),
                    ["code"] = Param("string", "The OTP code the user received.")
                },
                RequiredParameters = new[] { "phone_number", "code" }
            },
            new ToolDefinition
            {
                Name = "tgn.auth.push_to_app",
                Description = "Trigger a push-to-app biometric approval on the user's mobile device for a web login or sensitive action.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["session_id"] = Param("string", "The web session awaiting approval."),
                    ["reason"] = Param("string", "Human-readable reason shown to the user on the device.")
                },
                RequiredParameters = new[] { "session_id", "reason" }
            }
        };

        // ============================================================================
        // BidBaasAPI — auctions
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> BidBaas() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.bidbaas.list_active_auctions",
                Description = "List currently active BidBaas auctions, optionally filtered by category or location.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["category"] = Param("string", "Optional category filter, e.g. 'electronics', 'vehicles'."),
                    ["country_code"] = Param("string", "Optional ISO-3166 country code."),
                    ["limit"] = Param("number", "Max number of auctions to return. Default 25.")
                },
                RequiredParameters = System.Array.Empty<string>()
            },
            new ToolDefinition
            {
                Name = "tgn.bidbaas.place_bid",
                Description = "Place a bid on an active BidBaas auction.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["auction_id"] = Param("string", "Auction identifier."),
                    ["amount"] = Param("number", "Bid amount in the auction's listed currency."),
                    ["currency"] = Param("string", "ISO-4217 currency code, e.g. 'ZAR', 'USD'.")
                },
                RequiredParameters = new[] { "auction_id", "amount", "currency" }
            },
            new ToolDefinition
            {
                Name = "tgn.bidbaas.get_auction_details",
                Description = "Get full details for a specific auction including current top bid, time remaining, and seller info.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["auction_id"] = Param("string", "Auction identifier.")
                },
                RequiredParameters = new[] { "auction_id" }
            }
        };

        // ============================================================================
        // BillPaymentAPI — utility/bill payments
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> BillPayment() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.billpayment.list_billers",
                Description = "List available billers (utilities, telcos, councils) the user can pay.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["country_code"] = Param("string", "ISO-3166 country code, e.g. 'ZA'."),
                    ["category"] = Param("string", "Optional category filter, e.g. 'water', 'rates', 'data'.")
                },
                RequiredParameters = new[] { "country_code" }
            },
            new ToolDefinition
            {
                Name = "tgn.billpayment.pay_bill",
                Description = "Pay a bill for a specified biller using the user's wallet balance.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["biller_id"] = Param("string", "Biller identifier from list_billers."),
                    ["account_number"] = Param("string", "User's account number with that biller."),
                    ["amount"] = Param("number", "Amount to pay."),
                    ["currency"] = Param("string", "ISO-4217 currency code.")
                },
                RequiredParameters = new[] { "biller_id", "account_number", "amount", "currency" }
            }
        };

        // ============================================================================
        // BlockchainAPI — Aether / SDPKT blockchain
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Blockchain() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.blockchain.get_transaction",
                Description = "Look up a SDPKT/Aether on-chain transaction by hash.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["tx_hash"] = Param("string", "Transaction hash.")
                },
                RequiredParameters = new[] { "tx_hash" }
            },
            new ToolDefinition
            {
                Name = "tgn.blockchain.get_address_info",
                Description = "Get on-chain info about an Aether address (balance, recent activity).",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["address"] = Param("string", "Aether wallet address.")
                },
                RequiredParameters = new[] { "address" }
            }
        };

        // ============================================================================
        // ButlerAPI — Butler/B! orchestration server-side
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Butler() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.butler.log_interaction",
                Description = "Log a B!/Butler interaction for analytics and personalisation.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["intent"] = Param("string", "Detected intent name."),
                    ["transcript"] = Param("string", "Raw user utterance, redacted as needed."),
                    ["success"] = Param("boolean", "Whether the action succeeded.")
                },
                RequiredParameters = new[] { "intent", "transcript", "success" }
            },
            new ToolDefinition
            {
                Name = "tgn.butler.get_user_context",
                Description = "Fetch the server-side context for the current user (recent intents, preferences, capabilities).",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["user_id"] = Param("string", "Target user ID. Use 'me' for the current user.")
                },
                RequiredParameters = new[] { "user_id" }
            }
        };

        // ============================================================================
        // CircleAetherAPI — mesh network
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> CircleAether() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.circleaether.get_node_status",
                Description = "Get current mesh-node status (peers, throughput, region) for the authenticated device.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["device_id"] = Param("string", "Device identifier. Use 'this' for the current device.")
                },
                RequiredParameters = new[] { "device_id" }
            },
            new ToolDefinition
            {
                Name = "tgn.circleaether.list_nearby_peers",
                Description = "List mesh peers reachable from the current node, with link quality and tipping eligibility.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["max_peers"] = Param("number", "Max number of peers to return. Default 25.")
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // EcommerceAPI — generic ecommerce
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Ecommerce() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.ecommerce.search_products",
                Description = "Search the unified product catalogue across merchants in the ecosystem.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["query"] = Param("string", "Free-text search query."),
                    ["category"] = Param("string", "Optional category filter."),
                    ["max_price"] = Param("number", "Optional maximum price."),
                    ["currency"] = Param("string", "ISO-4217 currency code.")
                },
                RequiredParameters = new[] { "query" }
            },
            new ToolDefinition
            {
                Name = "tgn.ecommerce.get_product",
                Description = "Get full product details by ID, including stock, variants, and merchant info.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["product_id"] = Param("string", "Product identifier.")
                },
                RequiredParameters = new[] { "product_id" }
            }
        };

        // ============================================================================
        // ElectricityAPI — prepaid electricity
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Electricity() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.electricity.buy_token",
                Description = "Buy prepaid electricity for a meter and return the STS token to enter into the meter.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["meter_number"] = Param("string", "11-digit meter number."),
                    ["amount"] = Param("number", "Amount to spend on electricity."),
                    ["currency"] = Param("string", "ISO-4217 currency code, typically 'ZAR'.")
                },
                RequiredParameters = new[] { "meter_number", "amount", "currency" }
            },
            new ToolDefinition
            {
                Name = "tgn.electricity.list_recent_purchases",
                Description = "List the user's recent prepaid-electricity purchases.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["limit"] = Param("number", "Max number of purchases to return. Default 10.")
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // GeoAPI — geocoding (address <-> coordinates)
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Geo() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.geo.get_user_location",
                Description = "Get the authenticated user's current best-known location (lat/lng, accuracy, country).",
                Parameters = new Dictionary<string, ToolParameter>(),
                RequiredParameters = System.Array.Empty<string>()
            },
            new ToolDefinition
            {
                Name = "tgn.geo.geocode_address",
                Description = "Convert a human-readable address to coordinates.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["address"] = Param("string", "Free-text address to geocode."),
                    ["country_code"] = Param("string", "Optional ISO-3166 country bias.")
                },
                RequiredParameters = new[] { "address" }
            }
        };

        // ============================================================================
        // GlocellAPI — Glocell retail trade
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Glocell() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.glocell.list_products",
                Description = "List Glocell retail products (airtime, data, vouchers) available to the user.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["category"] = Param("string", "Optional category filter, e.g. 'airtime', 'data'.")
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // IncentivesAPI — gamification / Qi rewards
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Incentives() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.incentives.get_qi_balance",
                Description = "Get the user's current Qi (and Karma) balance and earning streak.",
                Parameters = new Dictionary<string, ToolParameter>(),
                RequiredParameters = System.Array.Empty<string>()
            },
            new ToolDefinition
            {
                Name = "tgn.incentives.list_active_quests",
                Description = "List quests/challenges the user can complete to earn Qi.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["limit"] = Param("number", "Max number of quests to return. Default 10.")
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // KiffStoreAPI — KiffStore
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> KiffStore() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.kiffstore.search_items",
                Description = "Search KiffStore listings.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["query"] = Param("string", "Free-text search query."),
                    ["limit"] = Param("number", "Max number of results. Default 25.")
                },
                RequiredParameters = new[] { "query" }
            }
        };

        // ============================================================================
        // LedgerAPI — financial ledger
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Ledger() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.ledger.get_account_balance",
                Description = "Get the running balance for a ledger account belonging to the user.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["account_id"] = Param("string", "Ledger account identifier.")
                },
                RequiredParameters = new[] { "account_id" }
            },
            new ToolDefinition
            {
                Name = "tgn.ledger.list_entries",
                Description = "List ledger entries for an account in reverse chronological order.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["account_id"] = Param("string", "Ledger account identifier."),
                    ["limit"] = Param("number", "Max number of entries to return. Default 50.")
                },
                RequiredParameters = new[] { "account_id" }
            }
        };

        // ============================================================================
        // LocalizationAPI — translations / 21 countries
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Localization() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.localization.translate_text",
                Description = "Translate a piece of text from one language to another using the ecosystem translation service.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["text"] = Param("string", "Text to translate."),
                    ["source_language"] = Param("string", "ISO-639-1 source code or 'auto' for auto-detect."),
                    ["target_language"] = Param("string", "ISO-639-1 target code, e.g. 'en', 'zu', 'fr'.")
                },
                RequiredParameters = new[] { "text", "target_language" }
            },
            new ToolDefinition
            {
                Name = "tgn.localization.list_supported_languages",
                Description = "List all language codes supported by the ecosystem.",
                Parameters = new Dictionary<string, ToolParameter>(),
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // MapsAPI — DataAcuity maps (rendering / tiles / styles)
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Maps() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.maps.geocode",
                Description = "Forward-geocode an address to coordinates via DataAcuity.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["address"] = Param("string", "Free-text address.")
                },
                RequiredParameters = new[] { "address" }
            },
            new ToolDefinition
            {
                Name = "tgn.maps.reverse_geocode",
                Description = "Reverse-geocode coordinates to an address.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["latitude"] = Param("number", "Latitude in decimal degrees."),
                    ["longitude"] = Param("number", "Longitude in decimal degrees.")
                },
                RequiredParameters = new[] { "latitude", "longitude" }
            }
        };

        // ============================================================================
        // MapsDataAPI — map data (POIs, routes, layers)
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> MapsData() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.mapsdata.search_pois",
                Description = "Search points of interest near a location, filtered by category.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["latitude"] = Param("number", "Latitude in decimal degrees."),
                    ["longitude"] = Param("number", "Longitude in decimal degrees."),
                    ["radius_meters"] = Param("number", "Search radius in metres. Default 1000."),
                    ["category"] = Param("string", "Optional POI category, e.g. 'pharmacy', 'fuel'.")
                },
                RequiredParameters = new[] { "latitude", "longitude" }
            }
        };

        // ============================================================================
        // MediaAPI — uploads / images
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Media() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.media.create_upload_url",
                Description = "Create a pre-signed URL the client can PUT a media file to. Does not upload the file itself.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["mime_type"] = Param("string", "MIME type of the file, e.g. 'image/jpeg'."),
                    ["size_bytes"] = Param("number", "File size in bytes.")
                },
                RequiredParameters = new[] { "mime_type", "size_bytes" }
            },
            new ToolDefinition
            {
                Name = "tgn.media.get_media",
                Description = "Get metadata and a viewable URL for a previously uploaded media item.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["media_id"] = Param("string", "Media identifier.")
                },
                RequiredParameters = new[] { "media_id" }
            }
        };

        // ============================================================================
        // MessagingAPI — TxTMe messaging
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Messaging() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.messaging.send_message",
                Description = "Send a TxTMe message to a contact or conversation.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["recipient"] = Param("string", "Recipient identifier - phone number (E.164) or user_id."),
                    ["body"] = Param("string", "Message body."),
                    ["conversation_id"] = Param("string", "Optional existing conversation to post into.")
                },
                RequiredParameters = new[] { "recipient", "body" }
            },
            new ToolDefinition
            {
                Name = "tgn.messaging.list_conversations",
                Description = "List the user's active TxTMe conversations, most recent first.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["limit"] = Param("number", "Max number of conversations to return. Default 25.")
                },
                RequiredParameters = System.Array.Empty<string>()
            },
            new ToolDefinition
            {
                Name = "tgn.messaging.get_messages",
                Description = "Get messages in a specific conversation, most recent first.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["conversation_id"] = Param("string", "Conversation identifier."),
                    ["limit"] = Param("number", "Max number of messages to return. Default 50.")
                },
                RequiredParameters = new[] { "conversation_id" }
            }
        };

        // ============================================================================
        // NotificationAPI — push notifications
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Notification() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.notification.send_push",
                Description = "Send a push notification to a user's registered devices.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["user_id"] = Param("string", "Target user ID."),
                    ["title"] = Param("string", "Notification title."),
                    ["body"] = Param("string", "Notification body text."),
                    ["data"] = Param("object", "Optional structured payload for the app to handle.")
                },
                RequiredParameters = new[] { "user_id", "title", "body" }
            },
            new ToolDefinition
            {
                Name = "tgn.notification.list_for_user",
                Description = "List recent in-app notifications for the authenticated user.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["unread_only"] = Param("boolean", "If true, return only unread notifications. Default false."),
                    ["limit"] = Param("number", "Max number to return. Default 50.")
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // OpSupportAPI — operations support
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> OpSupport() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.opsupport.create_ticket",
                Description = "File a support ticket on the user's behalf.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["category"] = Param("string", "Ticket category.", new[] { "billing", "account", "bug", "feature_request", "other" }),
                    ["subject"] = Param("string", "Short subject line."),
                    ["body"] = Param("string", "Full description of the issue.")
                },
                RequiredParameters = new[] { "category", "subject", "body" }
            },
            new ToolDefinition
            {
                Name = "tgn.opsupport.get_system_status",
                Description = "Get current system / API status (uptime, incidents).",
                Parameters = new Dictionary<string, ToolParameter>(),
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // PanikAPI — Panik SOS
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Panik() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.panik.trigger_sos",
                Description = "Trigger an SOS emergency alert. Notifies the user's panic contacts and optionally dispatches help.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["latitude"] = Param("number", "Current latitude in decimal degrees."),
                    ["longitude"] = Param("number", "Current longitude in decimal degrees."),
                    ["category"] = Param("string", "Type of emergency.", new[] { "medical", "crime", "fire", "accident", "other" }),
                    ["note"] = Param("string", "Optional short note describing the emergency.")
                },
                RequiredParameters = new[] { "latitude", "longitude", "category" }
            },
            new ToolDefinition
            {
                Name = "tgn.panik.cancel_sos",
                Description = "Cancel an in-progress SOS alert raised by the current user.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["alert_id"] = Param("string", "SOS alert identifier."),
                    ["reason"] = Param("string", "Optional reason for cancellation.")
                },
                RequiredParameters = new[] { "alert_id" }
            }
        };

        // ============================================================================
        // PayfastAPI — PayFast payments
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Payfast() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.payfast.create_payment",
                Description = "Create a PayFast payment intent and return the redirect URL the user should open.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["amount"] = Param("number", "Amount to charge."),
                    ["currency"] = Param("string", "ISO-4217 currency code, e.g. 'ZAR'."),
                    ["item_name"] = Param("string", "Short description shown on the PayFast page."),
                    ["return_url"] = Param("string", "URL to return to on completion.")
                },
                RequiredParameters = new[] { "amount", "currency", "item_name" }
            }
        };

        // ============================================================================
        // SdpktAPI — SDPKT wallet
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Sdpkt() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.sdpkt.get_balance",
                Description = "Get the user's SDPKT wallet balance, including any sub-balances (Qi, Karma, fiat-pegged).",
                Parameters = new Dictionary<string, ToolParameter>(),
                RequiredParameters = System.Array.Empty<string>()
            },
            new ToolDefinition
            {
                Name = "tgn.sdpkt.send_payment",
                Description = "Send an SDPKT payment to another user or wallet address.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["recipient"] = Param("string", "Recipient identifier - user ID, phone number (E.164), or wallet address."),
                    ["amount"] = Param("number", "Amount to send."),
                    ["currency"] = Param("string", "Currency code: 'SDPKT', 'QI', 'KARMA', or fiat ISO-4217."),
                    ["memo"] = Param("string", "Optional memo attached to the transaction.")
                },
                RequiredParameters = new[] { "recipient", "amount", "currency" }
            },
            new ToolDefinition
            {
                Name = "tgn.sdpkt.get_transactions",
                Description = "List the user's recent SDPKT wallet transactions.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["limit"] = Param("number", "Max number of transactions to return. Default 25.")
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // ShhMoneyAPI — discreet payments
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> ShhMoney() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.shhmoney.create_discreet_payment",
                Description = "Create a discreet ShhMoney payment - sender and recipient identifiers are hidden from third parties on the ledger surface.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["recipient"] = Param("string", "Recipient identifier."),
                    ["amount"] = Param("number", "Amount to send."),
                    ["currency"] = Param("string", "ISO-4217 currency code.")
                },
                RequiredParameters = new[] { "recipient", "amount", "currency" }
            }
        };

        // ============================================================================
        // SleptOnAPI — SleptOn news/content
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> SleptOn() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.slepton.list_stories",
                Description = "List recent SleptOn stories, optionally filtered by topic or country.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["topic"] = Param("string", "Optional topic filter."),
                    ["country_code"] = Param("string", "Optional ISO-3166 country code."),
                    ["limit"] = Param("number", "Max number of stories. Default 25.")
                },
                RequiredParameters = System.Array.Empty<string>()
            },
            new ToolDefinition
            {
                Name = "tgn.slepton.get_story",
                Description = "Get a SleptOn story's full body and metadata.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["story_id"] = Param("string", "Story identifier.")
                },
                RequiredParameters = new[] { "story_id" }
            }
        };

        // ============================================================================
        // SortedClothingAPI — clothing
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> SortedClothing() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.sortedclothing.search_items",
                Description = "Search the SortedClothing inventory.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["query"] = Param("string", "Free-text search query."),
                    ["size"] = Param("string", "Optional size filter."),
                    ["limit"] = Param("number", "Max results. Default 25.")
                },
                RequiredParameters = new[] { "query" }
            }
        };

        // ============================================================================
        // TagMeAPI — TagMe geo-tagging
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> TagMe() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.tagme.create_tag",
                Description = "Create a geo-tag at a location with optional note and visibility.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["latitude"] = Param("number", "Latitude in decimal degrees."),
                    ["longitude"] = Param("number", "Longitude in decimal degrees."),
                    ["note"] = Param("string", "Optional text note."),
                    ["visibility"] = Param("string", "Who can see the tag.", new[] { "public", "friends", "private" })
                },
                RequiredParameters = new[] { "latitude", "longitude" }
            },
            new ToolDefinition
            {
                Name = "tgn.tagme.list_nearby_tags",
                Description = "List geo-tags near a location.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["latitude"] = Param("number", "Latitude in decimal degrees."),
                    ["longitude"] = Param("number", "Longitude in decimal degrees."),
                    ["radius_meters"] = Param("number", "Radius in metres. Default 500.")
                },
                RequiredParameters = new[] { "latitude", "longitude" }
            }
        };

        // ============================================================================
        // TakemehomeAPI — travel comparison
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Takemehome() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.takemehome.search_flights",
                Description = "Search flights across multiple suppliers and return ranked options.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["origin"] = Param("string", "Origin IATA code or city name."),
                    ["destination"] = Param("string", "Destination IATA code or city name."),
                    ["depart_date"] = Param("string", "Departure date in YYYY-MM-DD."),
                    ["return_date"] = Param("string", "Optional return date in YYYY-MM-DD."),
                    ["passengers"] = Param("number", "Number of passengers. Default 1.")
                },
                RequiredParameters = new[] { "origin", "destination", "depart_date" }
            },
            new ToolDefinition
            {
                Name = "tgn.takemehome.search_stays",
                Description = "Search accommodation options for a destination and date range.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["destination"] = Param("string", "Destination city or area."),
                    ["check_in"] = Param("string", "Check-in date in YYYY-MM-DD."),
                    ["check_out"] = Param("string", "Check-out date in YYYY-MM-DD."),
                    ["guests"] = Param("number", "Number of guests. Default 1.")
                },
                RequiredParameters = new[] { "destination", "check_in", "check_out" }
            }
        };

        // ============================================================================
        // TheHotListAPI — curated list
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> TheHotList() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.thehotlist.list_entries",
                Description = "List curated 'hot list' entries, optionally filtered by category or country.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["category"] = Param("string", "Optional category filter."),
                    ["country_code"] = Param("string", "Optional ISO-3166 country code."),
                    ["limit"] = Param("number", "Max entries to return. Default 25.")
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // TheJobCenterAPI — jobs
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> TheJobCenter() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.thejobcenter.search_jobs",
                Description = "Search job postings.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["query"] = Param("string", "Free-text search query, e.g. 'plumber Cape Town'."),
                    ["country_code"] = Param("string", "Optional ISO-3166 country code."),
                    ["limit"] = Param("number", "Max results. Default 25.")
                },
                RequiredParameters = new[] { "query" }
            },
            new ToolDefinition
            {
                Name = "tgn.thejobcenter.apply",
                Description = "Submit an application to a job posting on the user's behalf.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["job_id"] = Param("string", "Job posting identifier."),
                    ["cover_note"] = Param("string", "Optional cover note.")
                },
                RequiredParameters = new[] { "job_id" }
            }
        };

        // ============================================================================
        // ThirdPartyAPI — generic third-party integrations
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> ThirdParty() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.thirdparty.list_integrations",
                Description = "List configured third-party integrations available to the user (e.g. Xero, Zapier-style hooks).",
                Parameters = new Dictionary<string, ToolParameter>(),
                RequiredParameters = System.Array.Empty<string>()
            },
            new ToolDefinition
            {
                Name = "tgn.thirdparty.invoke_integration",
                Description = "Invoke a registered third-party integration by name with a JSON payload.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["integration_name"] = Param("string", "Integration name from list_integrations."),
                    ["payload"] = Param("object", "JSON payload to forward to the integration.")
                },
                RequiredParameters = new[] { "integration_name", "payload" }
            }
        };

        // ============================================================================
        // TrustSealAPI — verification
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> TrustSeal() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.trustseal.get_status",
                Description = "Get the user's TrustSeal verification status (KYC level, document checks).",
                Parameters = new Dictionary<string, ToolParameter>(),
                RequiredParameters = System.Array.Empty<string>()
            },
            new ToolDefinition
            {
                Name = "tgn.trustseal.start_verification",
                Description = "Start a verification flow for a specified KYC level.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["level"] = Param("string", "Target KYC level.", new[] { "basic", "verified", "enhanced" })
                },
                RequiredParameters = new[] { "level" }
            }
        };

        // ============================================================================
        // WalletAPI — generic wallet
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Wallet() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.wallet.get_balance",
                Description = "Get the user's wallet balance(s) across all supported currencies.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["currency"] = Param("string", "Optional ISO-4217 currency to restrict the balance to.")
                },
                RequiredParameters = System.Array.Empty<string>()
            },
            new ToolDefinition
            {
                Name = "tgn.wallet.get_transactions",
                Description = "List the user's recent wallet transactions.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["currency"] = Param("string", "Optional ISO-4217 currency filter."),
                    ["limit"] = Param("number", "Max transactions to return. Default 25.")
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // WhatWeWantAPI — content stories
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> WhatWeWant() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.whatwewant.list_stories",
                Description = "List WhatWeWant stories, sorted by recency.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["topic"] = Param("string", "Optional topic filter."),
                    ["limit"] = Param("number", "Max stories to return. Default 25.")
                },
                RequiredParameters = System.Array.Empty<string>()
            },
            new ToolDefinition
            {
                Name = "tgn.whatwewant.get_story",
                Description = "Get a single WhatWeWant story's full body and metadata.",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["story_id"] = Param("string", "Story identifier.")
                },
                RequiredParameters = new[] { "story_id" }
            }
        };

        // ============================================================================
        // WolverineAPI — internal infra
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> Wolverine() => new[]
        {
            new ToolDefinition
            {
                Name = "tgn.wolverine.list_jobs",
                Description = "List background jobs visible to the user (status, last run, next run).",
                Parameters = new Dictionary<string, ToolParameter>
                {
                    ["status"] = Param("string", "Optional status filter.", new[] { "queued", "running", "succeeded", "failed" })
                },
                RequiredParameters = System.Array.Empty<string>()
            }
        };

        // ============================================================================
        // GetAllTools — concatenate every API's tools into a single canonical list.
        // ============================================================================

        public static IReadOnlyList<ToolDefinition> GetAllTools()
        {
            var all = new List<ToolDefinition>(96);
            all.AddRange(Account());
            all.AddRange(Audit());
            all.AddRange(Auth());
            all.AddRange(BidBaas());
            all.AddRange(BillPayment());
            all.AddRange(Blockchain());
            all.AddRange(Butler());
            all.AddRange(CircleAether());
            all.AddRange(Ecommerce());
            all.AddRange(Electricity());
            all.AddRange(Geo());
            all.AddRange(Glocell());
            all.AddRange(Incentives());
            all.AddRange(KiffStore());
            all.AddRange(Ledger());
            all.AddRange(Localization());
            all.AddRange(Maps());
            all.AddRange(MapsData());
            all.AddRange(Media());
            all.AddRange(Messaging());
            all.AddRange(Notification());
            all.AddRange(OpSupport());
            all.AddRange(Panik());
            all.AddRange(Payfast());
            all.AddRange(Sdpkt());
            all.AddRange(ShhMoney());
            all.AddRange(SleptOn());
            all.AddRange(SortedClothing());
            all.AddRange(TagMe());
            all.AddRange(Takemehome());
            all.AddRange(TheHotList());
            all.AddRange(TheJobCenter());
            all.AddRange(ThirdParty());
            all.AddRange(TrustSeal());
            all.AddRange(Wallet());
            all.AddRange(WhatWeWant());
            all.AddRange(Wolverine());
            return all;
        }
    }
}
