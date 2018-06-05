using System;
using Haasonline.Public.ExchangeDriver.Bittrex;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TradeServer.ScriptingDriver.ScriptApi.DataObjects;
using TradeServer.ScriptingDriver.ScriptApi.Interfaces;

namespace Haasonline.Public.ExchangeDriver.Tests
{
    [TestClass]
    public sealed class BittrexApiTest : TestBase
    {
        protected override IScriptApi Api { get; set; }

        protected override string PublicKey { get; set; }
        protected override string PrivateKey { get; set; }
        protected override string ExtraKey { get; set; }

        protected override IScriptMarket Market { get; set; }

        protected override bool AllowZeroPriceDecimals { get; set; } = false;
        protected override bool AllowZeroAmountDecimals { get; set; } = true;
        protected override bool AllowZeroFee { get; set; } = false;

        public BittrexApiTest() : base()
        {
            PublicKey = "";
            PrivateKey = "";
            ExtraKey = "";

            Api = new BittrexApi();

            Api.SetCredentials(PublicKey, PrivateKey, ExtraKey);

            Market = new ScriptMarket("BTC", "USDT", "");

            Start();
        }
    }
}
