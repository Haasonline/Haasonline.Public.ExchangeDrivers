using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web;
using Newtonsoft.Json.Linq;
using RestSharp;
using TradeServer.ScriptingDriver.ScriptApi.Enums;
using TradeServer.ScriptingDriver.ScriptApi.Interfaces;

namespace Haasonline.Public.ExchangeDriver.Bittrex
{
    public class BittrexApi : IScriptApi
    {
        private string _publicKey;
        private string _privateKey;
        private string _extra;

        private long _lastNonce;
        private readonly string _apiUrl = "https://bittrex.com/api/v1.1";
        private readonly HMACSHA512 _hmac = new HMACSHA512();

        public string PingAddress { get; set; }

        public int PollingSpeed { get; set; }
        public ScriptedExchangeType PlatformType { get; set; }

        public bool HasTickerBatchCalls { get; set; }
        public bool HasOrderbookBatchCalls { get; set; }
        public bool HasLastTradesBatchCalls { get; set; }

        public bool HasPrivateKey { get; set; }
        public bool HasExtraPrivateKey { get; set; }

        public event EventHandler<string> Error;
        public event EventHandler<IScriptTick> PriceUpdate;
        public event EventHandler<IScriptOrderbook> OrderbookUpdate;
        public event EventHandler<IScriptOrderbook> OrderbookCorrection;
        public event EventHandler<IScriptLastTrades> LastTradesUpdate;
        public event EventHandler<Dictionary<string, decimal>> WalletUpdate;
        public event EventHandler<Dictionary<string, decimal>> WalletCorrection;
        public event EventHandler<List<IScriptPosition>> PositionListUpdate;
        public event EventHandler<List<IScriptPosition>> PositionCorrection;
        public event EventHandler<List<IScriptOrder>> OpenOrderListUpdate;
        public event EventHandler<List<IScriptOrder>> OpenOrderCorrection;

        private readonly object _lockObject = new object();

        public BittrexApi()
        {
            PingAddress = "http://www.bittrex.com:80";
            PollingSpeed = 30;
            PlatformType = ScriptedExchangeType.Spot;

            HasTickerBatchCalls = true;
            HasOrderbookBatchCalls = false;
            HasLastTradesBatchCalls = false;

            HasPrivateKey = true;
            HasExtraPrivateKey = false;
        }

        public void SetCredentials(string publicKey, string privateKey, string extra)
        {
            _publicKey = publicKey;
            _privateKey = privateKey;
            _extra = extra;

            _hmac.Key = Encoding.UTF8.GetBytes(privateKey);
        }

        public void Connect()
        {
            // Start websocket is needed
        }

        public void Disconnect()
        {
            // Start websocket is needed
        }

        #region Public API
        public List<IScriptMarket> GetMarkets()
        {
            List<IScriptMarket> markets = null;

            try
            {
                var response = Query(false, "/public/getmarkets");

                if (response != null && response.Value<bool>("success"))
                {
                    markets = new List<IScriptMarket>();

                    foreach (var item in response.Value<JArray>("result"))
                        markets.Add(new Market(item as JObject));
                }
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return markets;
        }

        public List<IScriptMarket> GetMarginMarkets()
        {
            return null;
        }

        public IScriptTick GetTicker(IScriptMarket market)
        {
            IScriptTick ticker = null;

            try
            {
                var response = Query(false, "/public/getticker?", new Dictionary<string, string>()
                {
                    {"market",market.SecondaryCurrency.ToUpper() + "-" + market.PrimaryCurrency.ToUpper()},
                });

                if (response != null && response.Value<bool>("success"))
                    ticker = new Ticker(response.Value<JObject>("result"), market.PrimaryCurrency, market.SecondaryCurrency);
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return ticker;
        }

        public List<IScriptTick> GetAllTickers()
        {
            List<IScriptTick> tickers = null;

            try
            {
                var response = Query(false, "/public/getmarketsummaries");

                if (response != null && response.Value<bool>("success"))
                {
                    tickers = new List<IScriptTick>();

                    foreach (var item in response.Value<JArray>("result"))
                        tickers.Add(new Ticker(item as JObject));
                }
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return tickers;
        }

        public IScriptOrderbook GetOrderbook(IScriptMarket market)
        {
            IScriptOrderbook orderbook = null;

            try
            {
                var response = Query(false, "/public/getorderbook?", new Dictionary<string, string>()
                    {
                        {"market",market.SecondaryCurrency.ToUpper() + "-" + market.PrimaryCurrency.ToUpper()},
                        {"type","both"},
                        {"depth","50"}
                    });

                if (response != null && response.Value<bool>("success"))
                    orderbook = new Orderbook(response.Value<JObject>("result"));
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return orderbook;
        }

        public List<IScriptOrderbook> GetAllOrderbooks()
        {
            return null;
        }

        public IScriptLastTrades GetLastTrades(IScriptMarket market)
        {
            LastTradesContainer trades = null;

            try
            {
                var response = Query(false, "/public/getmarkethistory?", new Dictionary<string, string>()
                    {
                        {"market",market.SecondaryCurrency.ToUpper() + "-" + market.PrimaryCurrency.ToUpper()},
                        {"count","100"}
                    });

                if (response != null && response.Value<bool>("success"))
                {
                    trades = new LastTradesContainer();
                    trades.Market = market;
                    trades.Trades = response.Value<JArray>("result")
                        .Select(c => Trade.ParsePublicTrade(market, c as JObject))
                        .Cast<IScriptOrder>()
                        .OrderByDescending(c => c.Timestamp)
                        .ToList();
                }
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return trades;
        }

        public List<IScriptLastTrades> GetAllLastTrades()
        {
            return null;
        }
        #endregion

        #region Private API
        public Dictionary<string, decimal> GetWallet()
        {
            Dictionary<string, decimal> wallet = null;

            try
            {
                var response = Query(true, "/account/getbalances?", new Dictionary<string, string>()
                {
                    { "apikey", _publicKey }
                });

                if (response != null && response.Value<bool>("success"))
                    wallet = response.Value<JArray>("result")
                        .Where(c => Convert.ToDecimal(c.Value<string>("Available"), CultureInfo.InvariantCulture) > 0.0M)
                        .ToDictionary(c => c.Value<string>("Currency"), c => Convert.ToDecimal(c.Value<string>("Available"), CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return wallet;
        }

        public IScriptMarginWallet GetMarginWallet()
        {
            return null;
        }

        public List<IScriptOrder> GetOpenOrders()
        {
            List<IScriptOrder> orders = null;

            try
            {
                var response = Query(true, "/market/getopenorders?", new Dictionary<string, string>() { { "apikey", _publicKey } });

                if (response != null && response.Value<bool>("success"))
                {
                    orders = response.Value<JArray>("result")
                    .Select(item => Order.ParseOpenOrder(item as JObject))
                    .Cast<IScriptOrder>()
                    .ToList();
                }
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }


            return orders;
        }

        public List<IScriptPosition> GetPositions()
        {
            return null;
        }

        public List<IScriptOrder> GetTradeHistory()
        {
            List<IScriptOrder> trades = null;

            try
            {
                var parameters = new Dictionary<string, string>() {
                    { "apikey", _publicKey },
                    { "count", "1000" }
                };

                var response = Query(true, "/account/getorderhistory?", parameters);

                if (response != null && response.Value<bool>("success"))
                {
                    trades = response.Value<JArray>("result")
                        .Select(item => Trade.ParsePrivateTrades(item as JObject))
                        .Cast<IScriptOrder>()
                        .ToList();
                }
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return trades;
        }

        public string PlaceOrder(IScriptMarket market, ScriptedOrderType direction, decimal price, decimal amount, bool isMarketOrder, string template = "", bool hiddenOrder = false)
        {
            var result = "";

            try
            {
                var parameters = new Dictionary<string, string>
                    {
                        {"apikey", _publicKey},
                        {"market", market.SecondaryCurrency.ToUpper() + "-" + market.PrimaryCurrency.ToUpper()},
                        {"quantity", amount.ToString(CultureInfo.InvariantCulture)},
                        {"rate", price.ToString(CultureInfo.InvariantCulture)}
                    };

                var apiCall = "buylimit";
                if (direction == ScriptedOrderType.Sell)
                    apiCall = "selllimit";

                var response = Query(true, "/market/" + apiCall + "?", parameters);

                if (response != null && response.Value<bool>("success"))
                    result = response.Value<JObject>("result").Value<string>("uuid");
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return result;
        }
        public string PlaceOrder(IScriptMarket market, ScriptedLeverageOrderType direction, decimal price, decimal amount, decimal leverage, bool isMarketOrder, string template = "", bool isHiddenOrder = false)
        {
            return null;
        }

        public bool CancelOrder(IScriptMarket market, string orderId, bool isBuyOrder)
        {
            var result = false;

            try
            {
                var response = Query(true, "/market/cancel?", new Dictionary<string, string>()
                    {
                        { "apikey", _publicKey },
                        { "uuid", orderId }
                    });

                result = response != null && response.Value<bool>("success");
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return result;
        }

        public ScriptedOrderStatus GetOrderStatus(string orderId, IScriptMarket scriptMarket, decimal price, decimal amount, bool isBuyOrder)
        {
            var status = ScriptedOrderStatus.Unkown;

            try
            {
                var response = Query(true, "/account/getorder?", new Dictionary<string, string>() { { "apikey", _publicKey }, { "uuid", orderId } });

                if (response != null && response.Value<bool>("success"))
                    status = Order.ParseSingle(response.Value<JObject>("result")).Status;
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return status;
        }
        public IScriptOrder GetOrderDetails(string orderId, IScriptMarket market, decimal price, decimal amount, bool isBuyOrder)
        {
            // This might be called even when the order is in the open orders.
            // Make sure that the order is not open.

            Order order = null;

            try
            {
                var status = GetOrderStatus(orderId, market, price, amount, isBuyOrder);
                if (status != ScriptedOrderStatus.Completed && status != ScriptedOrderStatus.Cancelled)
                {
                    order = new Order();
                    order.Status = status;
                    return order;
                }

                Thread.Sleep(500);

                var history = GetTradeHistory();
                if (history == null)
                    return null;

                var trades = history
                    .Where(c => c.OrderId == orderId)
                    .ToList();

                order = new Order();
                GetOrderDetailsFromTrades(market, orderId, amount, order, trades);
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return order;
        }
        #endregion

        #region Helpers
        public decimal GetContractValue(IScriptMarket pair, decimal price)
        {
            return 1;
        }

        public decimal GetMaxPositionAmount(IScriptMarket pair, decimal tickClose, Dictionary<string, decimal> wallet, decimal leverage, ScriptedLeverageSide scriptedLeverageSide)
        {
            return 1;
        }


        private void GetOrderDetailsFromTrades(IScriptMarket market, string orderId, decimal amount, Order order, List<IScriptOrder> trades)
        {
            order.OrderId = orderId;
            order.Market = market;

            if (order.Status == ScriptedOrderStatus.Unkown)
                order.Status = trades.Sum(c => c.AmountFilled) >= amount
                    ? ScriptedOrderStatus.Completed
                    : ScriptedOrderStatus.Cancelled;

            order.Price = GetAveragePrice(trades.ToList());
            order.Amount = amount;
            order.AmountFilled = trades.Sum(c => c.AmountFilled);
            order.AmountFilled = Math.Min(order.Amount, order.AmountFilled);

            order.FeeCost = trades.Sum(c => c.FeeCost);

            if (!trades.Any())
                return;

            order.Timestamp = trades.First().Timestamp;
            order.FeeCurrency = trades[0].FeeCurrency;
            order.IsBuyOrder = trades[0].IsBuyOrder;
        }

        private decimal GetAveragePrice(List<IScriptOrder> trades)
        {
            var totalVolume = trades.Sum(c => c.AmountFilled * c.Price);
            if (totalVolume == 0 || trades.Sum(c => c.AmountFilled) == 0M)
                return 0M;

            return totalVolume / trades.Sum(c => c.AmountFilled);
        }
        #endregion

        #region Events
        private void OnError(string exMessage)
        {
            if (Error != null)
                Error(this, exMessage);
        }

        private void OnPriceUpdate(IScriptTick e)
        {
            if (PriceUpdate != null)
                PriceUpdate(this, e);
        }
        private void OnOrderbookUpdate(IScriptOrderbook e)
        {
            if (OrderbookUpdate != null)
                OrderbookUpdate(this, e);
        }
        private void OnOrderbookCorrection(IScriptOrderbook e)
        {
            if (OrderbookCorrection != null)
                OrderbookCorrection(this, e);
        }
        private void OnLastTradesUpdate(IScriptLastTrades e)
        {
            if (LastTradesUpdate != null)
                LastTradesUpdate(this, e);
        }

        private void OnWalletUpdate(Dictionary<string, decimal> e)
        {
            if (WalletUpdate != null)
                WalletUpdate(this, e);
        }
        private void OnWalletCorrection(Dictionary<string, decimal> e)
        {
            if (WalletCorrection != null)
                WalletCorrection(this, e);
        }

        private void OnOpenOrderListUpdate(List<IScriptOrder> e)
        {
            if (OpenOrderListUpdate != null)
                OpenOrderListUpdate(this, e);
        }
        private void OnOpenOrderCorrection(List<IScriptOrder> e)
        {
            if (OpenOrderCorrection != null)
                OpenOrderCorrection(this, e);
        }

        private void OnPositionListUpdate(List<IScriptPosition> e)
        {
            if (PositionListUpdate != null)
                PositionListUpdate(this, e);
        }
        private void OnPositionCorrection(List<IScriptPosition> e)
        {
            if (PositionCorrection != null)
                PositionCorrection(this, e);
        }
        #endregion

        #region Reset API Functions
        private JToken Query(bool authenticate, string methodName, Dictionary<string, string> args = null)
        {
            if (args == null)
                args = new Dictionary<string, string>();

            var dataStr = BuildPostData(args);

            if (Monitor.TryEnter(_lockObject, 30000))
                try
                {
                    if (authenticate)
                    {
                        //Add extra nonce-header
                        args.Add("nonce", GetNonce().ToString());
                        dataStr = BuildPostData(args);
                    }

                    string url = _apiUrl + methodName + dataStr;

                    RestClient client = new RestClient(url);
                    RestRequest request = new RestRequest();

                    if (authenticate)
                    {
                        var data = Encoding.UTF8.GetBytes(url);
                        request.AddHeader("apisign", ByteToString(_hmac.ComputeHash(data)).ToLower());
                    }

                    var response = client.Execute(request).Content;
                    return JToken.Parse(response);
                }
                catch (Exception ex)
                {
                    OnError(ex.Message);
                }
                finally
                {
                    Monitor.Exit(_lockObject);
                }

            return null;
        }

        private static string BuildPostData(Dictionary<string, string> d)
        {
            string s = "";
            for (int i = 0; i < d.Count; i++)
            {
                var item = d.ElementAt(i);
                var key = item.Key;
                var val = item.Value;

                s += key + "=" + HttpUtility.UrlEncode(val);

                if (i != d.Count - 1)
                    s += "&";
            }
            return s;
        }
        private Int64 GetNonce()
        {
            var temp = DateTime.UtcNow.Ticks;
            if (temp <= _lastNonce)
                temp = _lastNonce + 1;
            _lastNonce = temp;
            return _lastNonce;
        }

        private static string ByteToString(byte[] buff)
        {
            return buff.Aggregate("", (current, t) => current + t.ToString("X2"));
        }
        public static byte[] StringToByteArray(String hex)
        {
            var numberChars = hex.Length;
            var bytes = new byte[numberChars / 2];
            for (var i = 0; i < numberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }
        #endregion
    }

    public class Market : IScriptMarket
    {
        public string PrimaryCurrency { get; set; }
        public string SecondaryCurrency { get; set; }
        public decimal Fee { get; set; }
        public int PriceDecimals { get; set; }
        public int AmountDecimals { get; set; }
        public decimal MinimumTradeAmount { get; set; }
        public decimal MinimumTradeVolume { get; set; }

        // Not relavent for spot
        public DateTime SettlementDate { get; set; }
        public List<decimal> Leverage { get; set; }
        public string UnderlyingCurrency { get; set; }
        public string ContractName { get; set; }

        public Market(JObject data)
        {
            try
            {
                SettlementDate = DateTime.Now;
                Leverage = new List<decimal>();
                ContractName = "";

                PrimaryCurrency = data.Value<string>("MarketCurrency");
                SecondaryCurrency = data.Value<string>("BaseCurrency");
                UnderlyingCurrency = PrimaryCurrency;
                Fee = 0.25M;
                PriceDecimals = 8;
                MinimumTradeVolume = 0.0005M;

                string minTradeSize = data.Value<string>("MinTradeSize");
                MinimumTradeAmount = Decimal.Parse(minTradeSize, NumberStyles.Float, CultureInfo.InvariantCulture);

                AmountDecimals = 1;
                if (minTradeSize.Contains("."))
                    AmountDecimals = minTradeSize.Split('.')[1].Length;
                else
                    AmountDecimals = 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public Market(string primaryCurrency, string secondaryCurrency)
        {
            PrimaryCurrency = primaryCurrency;
            SecondaryCurrency = secondaryCurrency;
        }

        public virtual decimal ParsePrice(decimal price)
        {
            return Math.Round(price, PriceDecimals);
        }

        public virtual decimal ParseAmount(decimal price)
        {
            return Math.Round(price, AmountDecimals);
        }

        public virtual int GetPriceDecimals(decimal price)
        {
            return PriceDecimals;
        }

        public virtual int GetAmountDecimals(decimal price)
        {
            return AmountDecimals;
        }

        public bool IsAmountEnough(decimal price, decimal amount)
        {
            return amount > MinimumTradeAmount && amount * price >= MinimumTradeVolume;
        }
    }

    public class Ticker : IScriptTick
    {
        public IScriptMarket Market { get; set; }

        public decimal Close { get; set; }
        public decimal BuyPrice { get; set; }
        public decimal SellPrice { get; set; }

        public Ticker(JObject o, string primairy = "", string secondairy = "")
        {
            if (string.IsNullOrEmpty(primairy))
            {
                var pair = o.Value<string>("MarketName").Split('-');
                primairy = pair[1];
                secondairy = pair[0];
            }

            var close = o.Value<string>("Last");
            var buyPrice = o.Value<string>("Ask");
            var sellPrice = o.Value<string>("Bid");

            Market = new Market(primairy, secondairy);

            if (close != null)
                Close = Decimal.Parse(close, NumberStyles.Float, CultureInfo.InvariantCulture);

            if (buyPrice != null)
                BuyPrice = Decimal.Parse(buyPrice, NumberStyles.Float, CultureInfo.InvariantCulture);

            if (sellPrice != null)
                SellPrice = Decimal.Parse(sellPrice, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    public class Orderbook : IScriptOrderbook
    {
        public List<IScriptOrderbookRecord> Asks { get; set; }
        public List<IScriptOrderbookRecord> Bids { get; set; }

        public Orderbook(JObject o)
        {
            Bids = o.Value<JArray>("buy")
                .Select(c => OrderInfo.Parse(c as JObject))
                .ToList<IScriptOrderbookRecord>();

            Asks = o.Value<JArray>("sell")
                .Select(c => OrderInfo.Parse(c as JObject))
                .ToList<IScriptOrderbookRecord>();
        }
    }

    public class OrderInfo : IScriptOrderbookRecord
    {
        public decimal Price { get; set; }
        public decimal Amount { get; set; }

        public static OrderInfo Parse(JObject o)
        {
            if (o == null)
                return null;

            var r = new OrderInfo()
            {
                Price = Decimal.Parse(o.Value<string>("Rate"), NumberStyles.Float, CultureInfo.InvariantCulture),
                Amount = Decimal.Parse(o.Value<string>("Quantity"), NumberStyles.Float, CultureInfo.InvariantCulture)
            };

            return r;
        }
    }

    public class LastTradesContainer : IScriptLastTrades
    {
        public IScriptMarket Market { get; set; }
        public List<IScriptOrder> Trades { get; set; }
    }

    public class Order : IScriptOrder
    {
        public IScriptMarket Market { get; set; }
        public string OrderId { get; set; }
        public string ExecutingId { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
        public decimal AmountFilled { get; set; }
        public decimal FeeCost { get; set; }
        public string FeeCurrency { get; set; }
        public bool IsBuyOrder { get; set; }
        public ScriptedLeverageOrderType Direction { get; set; }
        public ScriptedOrderStatus Status { get; set; }
        public string ExtraInfo1 { get; set; }

        public Order()
        {
            Status = ScriptedOrderStatus.Unkown;
        }

        public static Order ParseOpenOrder(JObject o)
        {
            if (o == null)
                return null;

            string[] pair = o.Value<string>("Exchange").Split('-');

            var r = new Order()
            {
                Market = new Market(pair[1], pair[0]),

                OrderId = o.Value<string>("OrderUuid"),

                Price = Convert.ToDecimal(o.Value<string>("Limit"), CultureInfo.InvariantCulture),
                Amount = Convert.ToDecimal(o.Value<string>("Quantity"), CultureInfo.InvariantCulture),
                AmountFilled = Convert.ToDecimal(o.Value<string>("Quantity"), CultureInfo.InvariantCulture) -
                               Convert.ToDecimal(o.Value<string>("QuantityRemaining"), CultureInfo.InvariantCulture),

                Status = ScriptedOrderStatus.Executing
            };

            if (o.Value<string>("OrderType") != null)
                r.IsBuyOrder = o.Value<string>("OrderType").ToLower() == "limit_buy";


            return r;
        }

        public static Order ParseSingle(JObject o)
        {
            if (o == null)
                return null;

            Order order = ParseOpenOrder(o);
            order.IsBuyOrder = o.Value<string>("Type").IndexOf("limit_buy", StringComparison.Ordinal) > 0;

            if (Convert.ToDecimal(o.Value<string>("QuantityRemaining"), CultureInfo.InvariantCulture) == 0.0M)
                order.Status = ScriptedOrderStatus.Completed;

            else if (!o.Value<bool>("IsOpen"))
                order.Status = ScriptedOrderStatus.Cancelled;

            else if (o.Value<bool>("IsOpen"))
                order.Status = ScriptedOrderStatus.Executing;

            if (o.Property("CancelInitiated") != null && o.Value<bool>("CancelInitiated"))
                order.Status = ScriptedOrderStatus.Cancelled;

            return order;
        }
    }

    public class Trade : IScriptOrder
    {
        public IScriptMarket Market { get; set; }
        public string OrderId { get; set; }
        public string ExecutingId { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
        public decimal AmountFilled { get; set; }
        public decimal FeeCost { get; set; }
        public string FeeCurrency { get; set; }
        public bool IsBuyOrder { get; set; }
        public ScriptedLeverageOrderType Direction { get; set; }
        public ScriptedOrderStatus Status { get; set; }

        public static Trade ParsePrivateTrades(JObject o)
        {
            if (o == null)
                return null;

            string[] pair = o.Value<string>("Exchange").Split('-');

            var r = new Trade()
            {
                Market = new Market(pair[1], pair[0]),

                OrderId = o.Value<string>("OrderUuid"),
                Timestamp = DateTime.ParseExact(o.Value<string>("Closed"), "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture),

                Price = Decimal.Parse(o.Value<string>("Price"), NumberStyles.Float, CultureInfo.InvariantCulture),
                Amount = Decimal.Parse(o.Value<string>("Quantity"), NumberStyles.Float, CultureInfo.InvariantCulture),
                AmountFilled = Decimal.Parse(o.Value<string>("Quantity"), NumberStyles.Float, CultureInfo.InvariantCulture) - Decimal.Parse(o.Value<string>("QuantityRemaining"), NumberStyles.Float, CultureInfo.InvariantCulture),

                FeeCurrency = pair[0],
                FeeCost = Decimal.Parse(o.Value<string>("Commission"), NumberStyles.Float, CultureInfo.InvariantCulture),

                IsBuyOrder = o.Value<string>("OrderType").ToLower().IndexOf("buy", StringComparison.Ordinal) > -1,
            };

            // Bittrex send total pays price. Small correction to be made here.
            r.Price = Math.Round(r.Price / r.Amount, 8);

            return r;
        }

        public static Trade ParsePublicTrade(IScriptMarket market, JObject o)
        {
            if (o == null)
                return null;

            var r = new Trade()
            {
                Market = market,

                Timestamp = DateTime.ParseExact(o.Value<string>("TimeStamp"), "MM/dd/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),

                Price = Decimal.Parse(o.Value<string>("Price"), NumberStyles.Float, CultureInfo.InvariantCulture),
                Amount = Decimal.Parse(o.Value<string>("Quantity"), NumberStyles.Float, CultureInfo.InvariantCulture),

                IsBuyOrder = o.Value<string>("OrderType").ToLower() == "buy"
            };

            return r;
        }
    }
}