using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TradeServer.ScriptingDriver.ScriptApi.Enums;
using TradeServer.ScriptingDriver.ScriptApi.Interfaces;

namespace Haasonline.Public.ExchangeDriver.Tests
{
    public abstract class TestBase
    {
        protected virtual int SleepAfterTest { get; set; } = 1000;
        protected virtual bool IgnoreErrors { get; set; }

        protected abstract IScriptApi Api { get; set; }

        protected abstract string PublicKey { get; set; }
        protected abstract string PrivateKey { get; set; }
        protected abstract string ExtraKey { get; set; }
        protected abstract IScriptMarket Market { get; set; }

        protected abstract bool AllowZeroPriceDecimals { get; set; }
        protected abstract bool AllowZeroAmountDecimals { get; set; }
        protected abstract bool AllowZeroFee { get; set; }

        public void Start()
        {
            ((IScriptApi)Api).Error += OnError;

            Api.GetMarkets();
        }

        private void OnError(object sender, string e)
        {
            Assert.IsFalse(true, e);
        }

        #region Markets

        [TestMethod]
        public void Public_GetMarkets()
        {
            List<IScriptMarket> markets = Api.GetMarkets();

            Assert.IsNotNull(markets, "Bad API response");
            Assert.IsTrue(markets.Count > 0, "Empty market list");

            foreach (var market in markets)
                AssertMarket(market);

            Thread.Sleep(SleepAfterTest);
        }

        [TestMethod]
        public void Public_GetMarginMarket()
        {
            if (Api.PlatformType != ScriptedExchangeType.Margin)
                return;

            List<IScriptMarket> markets = Api.GetMarginMarkets();

            Assert.IsNotNull(markets, "Bad API response");
            Assert.IsTrue(markets.Count > 0, "Empty market list");

            foreach (var market in markets)
                AssertMarket(market);

            Thread.Sleep(SleepAfterTest);
        }

        private void AssertMarket(IScriptMarket market)
        {
            Assert.IsTrue(market.PrimaryCurrency != market.SecondaryCurrency, "Double currency detected");

            if (Api.PlatformType != ScriptedExchangeType.Spot)
                Assert.IsTrue(market.Leverage.Count > 0, "Missing leverage levels");

            if (!AllowZeroPriceDecimals)
                Assert.IsTrue(market.PriceDecimals != 0.0M, $"Zero price decimals detected  {market.PrimaryCurrency} {market.SecondaryCurrency}");

            if (!AllowZeroAmountDecimals)
                Assert.IsTrue(market.AmountDecimals != 0.0M, $"Zero amount decimals detected {market.PrimaryCurrency} {market.SecondaryCurrency}");

            if (!AllowZeroFee)
                Assert.IsTrue(market.Fee > 0.0M, $"Missing fee percentage: {market}");
        }


        #endregion

        #region Tick Data
        [TestMethod]
        public void Public_GetTicker()
        {
            var tick = Api.GetTicker(Market);

            AssertTick(tick);

            Thread.Sleep(SleepAfterTest);
        }

        [TestMethod]
        public void Public_GetTicker_All()
        {
            if (!Api.HasTickerBatchCalls)
                return;

            var ticks = Api.GetAllTickers();

            Assert.IsNotNull(ticks, "Bad API response");
            Assert.IsTrue(ticks.Any(), "Empty tick list");

            foreach (var tick in ticks)
                AssertTick(tick);

            Thread.Sleep(SleepAfterTest);
        }

        private void AssertTick(IScriptTick tick)
        {
            Assert.IsNotNull(tick, "Bad API response");
            Assert.IsTrue(tick.Close > 0.0M, "Close price is zero");

            if (tick.BuyPrice != 0.0M)
                Assert.IsTrue(tick.BuyPrice > tick.SellPrice, "Buy and sell price reversed");
        }

        #endregion

        #region Orderbook Data
        [TestMethod]
        public void Public_GetOrderbook()
        {
            var orderbook = Api.GetOrderbook(Market);

            AssertOrderbook(orderbook);

            Thread.Sleep(SleepAfterTest);
        }

        [TestMethod]
        public void Public_GetOrderbook_All()
        {
            if (!Api.HasOrderbookBatchCalls)
                return;

            var orderbooks = Api.GetAllOrderbooks();

            Assert.IsNotNull(orderbooks, "Bad API response");
            Assert.IsTrue(orderbooks.Count >= 2, "Empty orderbook list");

            foreach (var orderbook in orderbooks)
                AssertOrderbook(orderbook);

            Thread.Sleep(SleepAfterTest);
        }

        private void AssertOrderbook(IScriptOrderbook orderbook)
        {
            Assert.IsNotNull(orderbook, "Bad API response");
            Assert.IsNotNull(orderbook.Asks, "Asks null");
            Assert.IsNotNull(orderbook.Bids, "Bids null");
            Assert.IsTrue(orderbook.Asks.Count > 0, "No asks found");
            Assert.IsTrue(orderbook.Bids.Count > 0, "No bids found");

            Assert.IsTrue(orderbook.Bids[0].Price > 0.0M, "Bid price zero");
            Assert.IsTrue(orderbook.Bids[0].Amount > 0.0M, "Bid amount zero");

            Assert.IsTrue(orderbook.Bids[0].Price < orderbook.Asks[0].Price, "Bid is higher than ask");
            Assert.IsTrue(orderbook.Asks[0].Price < orderbook.Asks[1].Price, "Sorting issue");
            Assert.IsTrue(orderbook.Bids[0].Price > orderbook.Bids[1].Price, "Sorting issue 2");

            var bids = new Dictionary<decimal, decimal>();
            var asks = new Dictionary<decimal, decimal>();

            foreach (var order in orderbook.Asks)
            {
                if (!asks.ContainsKey(order.Price))
                {
                    asks.Add(order.Price, order.Amount);
                }
                else
                {
                    Assert.IsFalse(true, "Double price in orderbook: " + order.Price);
                }
            }

            foreach (var order in orderbook.Bids)
            {
                if (!bids.ContainsKey(order.Price))
                {
                    bids.Add(order.Price, order.Amount);
                }
                else
                {
                    Assert.IsFalse(true, "Double price in orderbook: " + order.Price);
                }
            }

        }
        #endregion

        #region Last Trades Data
        [TestMethod]
        public void Public_GetPublicTrades()
        {
            var trades = Api.GetLastTrades(Market);

            AssertTrades(trades);

            Thread.Sleep(SleepAfterTest);
        }

        public void Public_GetPublicTrades_All()
        {
            if (!Api.HasLastTradesBatchCalls)
                return;

            var trades = Api.GetAllLastTrades();

            Assert.IsNotNull(trades, "Bad API response");
            Assert.IsTrue(trades.Any(), "Empty trades list");

            foreach (var trade in trades)
                AssertTrades(trade);

            Thread.Sleep(SleepAfterTest);
        }

        private void AssertTrades(IScriptLastTrades trades)
        {
            Assert.IsNotNull(trades, "Bad API response");
            Assert.IsTrue(trades.Trades.Any(), "Empty trades list");
            foreach (var trade in trades.Trades)
            {
                Assert.IsTrue(trade.Price > 0.0M, "Zero price");
                Assert.IsTrue(trade.Amount > 0.0M, "Zero amount");
                Assert.IsTrue(trade.Timestamp > DateTime.UtcNow.AddMonths(-2), "Outdated timestamp, wrong parsing?");
            }
        }
        #endregion

        #region Wallet Data
        [TestMethod]
        public void Private_Wallet()
        {
            var wallet = Api.GetWallet();
            Assert.IsNotNull(wallet, "Bad API response on setup");

            foreach (var coin in wallet)
                Assert.IsTrue(coin.Value > 0, $"<=0 amount detected: {coin.Value} {coin.Key}");

            Thread.Sleep(SleepAfterTest);
        }

        [TestMethod]
        public void Private_WalletMargin()
        {
            if (Api.PlatformType != ScriptedExchangeType.Margin)
                return;

            var wallet = Api.GetWallet();
            Assert.IsNotNull(wallet, "Bad API response on setup");

            foreach (var coin in wallet)
                Assert.IsTrue(coin.Value > 0, $"-0 amount detected: {coin.Value} {coin.Key}");

            Thread.Sleep(SleepAfterTest);
        }
        #endregion

        #region Order Data

        [TestMethod]
        public void Private_OpenOrders()
        {
            var orders = Api.GetOpenOrders();

            AssertOpenOrder(orders);

            Thread.Sleep(SleepAfterTest);
        }

        private void AssertOpenOrder(List<IScriptOrder> openOrders)
        {
            Assert.IsNotNull(openOrders, "Bad API response");

            foreach (var order in openOrders)
            {
                Assert.IsFalse(string.IsNullOrEmpty(order.OrderId), "Missing Id");
                Assert.IsTrue(order.Price > 0M, "-0 Price detected");
                Assert.IsTrue(order.Amount > 0M, "-0 Amount detected");
                Assert.IsNotNull(order.Market, "null market detected");
                Assert.IsFalse(string.IsNullOrEmpty(order.Market.PrimaryCurrency), "null primary detected");
                Assert.IsFalse(string.IsNullOrEmpty(order.Market.SecondaryCurrency), "null secondary detected");

                Assert.IsTrue(order.Status == ScriptedOrderStatus.Executing, "Status mismatch");
            }
        }

        #endregion

        #region Trade History Data

        [TestMethod]
        public void Private_GetTradeHistory()
        {
            var history = Api.GetTradeHistory();
            AssertTradeHistory(history);
        }

        private void AssertTradeHistory(List<IScriptOrder> history)
        {
            Assert.IsNotNull(history, "Bad Api response");
            Assert.IsTrue(history.Count > 0, "Empty list");

            foreach (var order in history)
            {
                Assert.IsFalse(string.IsNullOrEmpty(order.OrderId), "Missing Id");
                Assert.IsTrue(order.Price > 0M, "-0 Price detected");
                Assert.IsTrue(order.Amount > 0M, "-0 Amount detected");
                Assert.IsNotNull(order.Market, "null market detected");
                Assert.IsFalse(string.IsNullOrEmpty(order.Market.PrimaryCurrency), "null primary detected");
                Assert.IsFalse(string.IsNullOrEmpty(order.Market.SecondaryCurrency), "null secondary detected");
            }
        }

        #endregion

        [TestCleanup]
        public void CleanUp()
        {
            Thread.Sleep(SleepAfterTest);
        }
    }
}