using System;
using System.IO;

using NUnit.Framework;
using System.Linq;
using Neo.Lux.Core;
using Neo.Lux.Utils;
using Neo.Lux.Cryptography;
using Imusify.SmartContract;
using System.Diagnostics;
using System.Numerics;
using Neo.Lux.Debugger;
using System.Collections.Generic;


namespace ImusifyTests
{
    public class TestEnviroment
    {
        public readonly Emulator api;
        public readonly KeyPair owner_keys;
        public readonly KeyPair team_keys;
        public readonly KeyPair[] whitelisted_buyerKeys;
        public readonly DebugClient debugger;
        public readonly NEP5 token;

        public TestEnviroment(int buyers_count)
        {
            debugger = new DebugClient();

            // this is the key for the NEO "issuer" in the virtual chain used for testing
            owner_keys = KeyPair.GenerateAddress();

            this.api = new Emulator(owner_keys);

            this.api.SetLogger(x => {
                if (api.Chain.HasDebugger)
                {
                    debugger.WriteLine(x);
                }
                Debug.WriteLine(x);
            });

            Transaction tx;

            // create a random key for the team
            team_keys = KeyPair.GenerateAddress();
            // since the real team address is hardcoded in the contract, use BypassKey to give same permissions to this key
            this.api.Chain.BypassKey(new UInt160(ImusifyContract.Company_Address), new UInt160(team_keys.address.AddressToScriptHash()));

            tx = api.SendAsset(owner_keys, team_keys.address, "GAS", 800);
            Assert.IsNotNull(tx);

            var balances = api.GetAssetBalancesOf(team_keys.address);
            Assert.IsTrue(balances.ContainsKey("GAS"));
            Assert.IsTrue(balances["GAS"] == 800);

            tx = api.DeployContract(team_keys, ContractTests.contract_script_bytes, "0710".HexToBytes(), 5, ContractPropertyState.HasStorage, "Imusify", "1.0", "imusify.io", "info@imusify.io", "Imusify Smart Contract");
            Assert.IsNotNull(tx);

            if (buyers_count > 0)
            {
                whitelisted_buyerKeys = new KeyPair[buyers_count];
                var indices = new int[buyers_count];

                var addresses = new HashSet<string>();
                for (int i = 0; i < buyers_count; i++)
                {
                    string address;
                    do
                    {
                        whitelisted_buyerKeys[i] = KeyPair.GenerateAddress();
                        address = whitelisted_buyerKeys[i].address;
                    } while (addresses.Contains(address));

                    addresses.Add(address);
                }

                for (int i = 0; i < buyers_count; i++)
                {
                    api.SendAsset(owner_keys, whitelisted_buyerKeys[i].address, "NEO", 100);

                    api.CallContract(team_keys, ContractTests.contract_script_hash, "whitelistAddFree", new object[] { whitelisted_buyerKeys[i].address.AddressToScriptHash() });
                }
            }

            this.token = new NEP5(api, ContractTests.contract_script_hash);

            // do deploy
            Assert.IsTrue(this.token.TotalSupply == 0);

            tx = TokenSale.Deploy(this.token, this.team_keys);
            Assert.IsNotNull(tx);

            var notifications = this.api.Chain.GetNotifications(tx);
            Assert.IsNotNull(notifications);
            Assert.IsTrue(notifications.Count == 3);

            var account = this.api.Chain.GetAccount(ContractTests.contract_script_hash);
            Assert.IsTrue(account.storage.entries.Count > 0);

            var fund_balance = this.token.BalanceOf(ImusifyContract.Fund_Address);
            Assert.IsTrue(fund_balance == (ImusifyContract.fund_supply / ImusifyContract.imu_decimals));

            var plaform_balance = this.token.BalanceOf(ImusifyContract.Platform_Address);
            Assert.IsTrue(plaform_balance == (ImusifyContract.platform_supply / ImusifyContract.imu_decimals));

            var expected_supply = (ImusifyContract.initial_supply / ImusifyContract.imu_decimals);
            Assert.IsTrue(this.token.TotalSupply == expected_supply);
        }

        #region UTILS
        public bool IsWhitelisted(byte[] script_hash)
        {
            var whitelist_result = api.InvokeScript(ContractTests.contract_script_hash, "whitelistCheckOne", new object[] { script_hash });
            if (whitelist_result == null)
            {
                return false;
            }

            var bytes = (byte[])whitelist_result.stack[0];
            var is_whitelisted = bytes != null && bytes.Length > 0 ? bytes[0] : 0;
            return is_whitelisted == 1;
        }

        public decimal GetBalanceOf(UInt160 script_hash, string symbol)
        {
            var balances = api.GetAssetBalancesOf(script_hash);
            return balances.ContainsKey(symbol) ? balances[symbol] : 0;
        }
        #endregion
    }

    [TestFixture]
    public class ContractTests
    {
        public static byte[] contract_script_bytes { get; set; }
        public static UInt160 contract_script_hash { get; set; }

        private string contract_folder;

        [OneTimeSetUp]
        public void FixtureSetUp()
        {
            var temp = TestContext.CurrentContext.TestDirectory.Split(new char[] { '\\', '/' }).ToList();

            for (int i = 0; i < 3; i++)
            {
                temp.RemoveAt(temp.Count - 1);
            }

            temp.Add("ImusifyContract");
            temp.Add("bin");
            temp.Add("Debug");

            contract_folder = String.Join("\\", temp.ToArray());

            contract_script_bytes = File.ReadAllBytes(contract_folder + "/ImusifyContract.avm");
            contract_script_hash = contract_script_bytes.ToScriptHash();

            Assert.IsNotNull(contract_script_bytes);
        }

        [Test]
        public void TestNEP5()
        {
            var env = new TestEnviroment(0);

            Assert.IsTrue(env.token.Name == "imusify Token");
            Assert.IsTrue(env.token.Symbol == "IMU");
            Assert.IsTrue(env.token.Decimals == 8);
        }

        [Test]
        public void TestSaleTime()
        {
            Assert.IsTrue(ImusifyContract.stage_1_start < ImusifyContract.stage_2_start);
            Assert.IsTrue(ImusifyContract.stage_2_start < ImusifyContract.stage_3_start);
            Assert.IsTrue(ImusifyContract.stage_3_start < ImusifyContract.stage_4_start);
            Assert.IsTrue(ImusifyContract.stage_4_start < ImusifyContract.stage_5_start);
            Assert.IsTrue(ImusifyContract.stage_5_start < ImusifyContract.stage_6_start);
            Assert.IsTrue(ImusifyContract.stage_6_start < ImusifyContract.sale_end);

            var stage1_date = ImusifyContract.stage_1_start.ToDateTime();
            Assert.IsTrue(stage1_date.Day == 1);
            Assert.IsTrue(stage1_date.Month == 6);
            Assert.IsTrue(stage1_date.Year == 2018);
            Assert.IsTrue(stage1_date.Hour == 2);
            Assert.IsTrue(stage1_date.Minute == 0);

            var stage2_date = ImusifyContract.stage_2_start.ToDateTime();
            Assert.IsTrue(stage2_date.Day == 1);
            Assert.IsTrue(stage2_date.Month == 7);
            Assert.IsTrue(stage2_date.Year == 2018);
            Assert.IsTrue(stage2_date.Hour == 2);
            Assert.IsTrue(stage2_date.Minute == 0);

            var stage3_date = ImusifyContract.stage_3_start.ToDateTime();
            Assert.IsTrue(stage3_date.Day == 3);
            Assert.IsTrue(stage3_date.Month == 7);
            Assert.IsTrue(stage3_date.Year == 2018);
            Assert.IsTrue(stage3_date.Hour == 2);
            Assert.IsTrue(stage3_date.Minute == 0);

            var stage4_date = ImusifyContract.stage_4_start.ToDateTime();
            Assert.IsTrue(stage4_date.Day == 10);
            Assert.IsTrue(stage4_date.Month == 7);
            Assert.IsTrue(stage4_date.Year == 2018);
            Assert.IsTrue(stage4_date.Hour == 2);
            Assert.IsTrue(stage4_date.Minute == 0);

            var stage5_date = ImusifyContract.stage_5_start.ToDateTime();
            Assert.IsTrue(stage5_date.Day == 17);
            Assert.IsTrue(stage5_date.Month == 7);
            Assert.IsTrue(stage5_date.Year == 2018);
            Assert.IsTrue(stage5_date.Hour == 2);
            Assert.IsTrue(stage5_date.Minute == 0);

            var stage6_date = ImusifyContract.stage_6_start.ToDateTime();
            Assert.IsTrue(stage6_date.Day == 24);
            Assert.IsTrue(stage6_date.Month == 7);
            Assert.IsTrue(stage6_date.Year == 2018);
            Assert.IsTrue(stage6_date.Hour == 2);
            Assert.IsTrue(stage6_date.Minute == 0);

            var stage_end_date = ImusifyContract.sale_end.ToDateTime();
            Assert.IsTrue(stage_end_date.Day == 3);
            Assert.IsTrue(stage_end_date.Month == 7);
            Assert.IsTrue(stage_end_date.Year == 2018);
            Assert.IsTrue(stage_end_date.Hour == 2);
            Assert.IsTrue(stage_end_date.Minute == 0);

        }

        [Test]
        public void FailBuyDuringSaleNoWhitelist()
        {
            var env = new TestEnviroment(0);

            var neo_amount = 5;

            var original_neo_balance = env.GetBalanceOf(contract_script_hash, "NEO");

            var random_buyerKeys = KeyPair.GenerateAddress();
            env.api.SendAsset(env.owner_keys, random_buyerKeys.address, "NEO", neo_amount);

            env.api.Chain.Time = ImusifyContract.stage_1_start + 1;

            var tx = TokenSale.MintTokens(env.token, random_buyerKeys, "NEO", neo_amount);
            Assert.IsNull(tx);

            var balance = env.token.BalanceOf(random_buyerKeys);
            Assert.IsTrue(balance == 0);

            var current_neo_balance = env.GetBalanceOf(contract_script_hash, "NEO");
            Assert.IsTrue(current_neo_balance == original_neo_balance);
        }

        [Test]
        public void FailBuyOutsideSalePeriod()
        {
            var env = new TestEnviroment(0);

            var random_buyerKeys = KeyPair.GenerateAddress();

            var original_balance = env.token.BalanceOf(random_buyerKeys);

            var neo_amount = 5;
            env.api.SendAsset(env.owner_keys, random_buyerKeys.address, "NEO", neo_amount);

            env.api.Chain.Time = ImusifyContract.stage_1_start - 100;

            var tx = TokenSale.MintTokens(env.token, random_buyerKeys, "NEO", neo_amount);

            Assert.IsNull(tx);

            var new_balance = env.token.BalanceOf(random_buyerKeys);
            Assert.IsTrue(new_balance == original_balance);
        }

        [Test]
        public void FailBuyOutsideSalePeriodEvenIfWhitelisted()
        {
            var env = new TestEnviroment(1);

            var n = 0;

            var original_balance = env.token.BalanceOf(env.whitelisted_buyerKeys[n]);

            var is_whitelisted = env.IsWhitelisted(env.whitelisted_buyerKeys[n].address.AddressToScriptHash());
            Assert.IsTrue(is_whitelisted);

            var neo_amount = 5;
            env.api.SendAsset(env.owner_keys, env.whitelisted_buyerKeys[n].address, "NEO", neo_amount);

            env.api.Chain.Time = ImusifyContract.stage_1_start - 100;

            var tx = TokenSale.MintTokens(env.token, env.whitelisted_buyerKeys[n], "NEO", neo_amount);

            Assert.IsNull(tx);

            var new_balance = env.token.BalanceOf(env.whitelisted_buyerKeys[n]);
            Assert.IsTrue(new_balance == original_balance);
        }

        [Test]
        public void TestBuyDuringSaleWhitelistedSinglePurchase()
        {
            var env = new TestEnviroment(1);

            var n = 0;

            var original_balance = env.token.BalanceOf(env.whitelisted_buyerKeys[n]);

            var is_whitelisted = env.IsWhitelisted(env.whitelisted_buyerKeys[n].address.AddressToScriptHash());
            Assert.IsTrue(is_whitelisted);

            env.api.Chain.Time = ImusifyContract.stage_1_start + 1;

            var neo_amount = 5;
            env.api.SendAsset(env.owner_keys, env.whitelisted_buyerKeys[n].address, "NEO", neo_amount);

            var tx = TokenSale.MintTokens(env.token, env.whitelisted_buyerKeys[n], "NEO", neo_amount);

            Assert.IsNotNull(tx);

            var notifications = env.api.Chain.GetNotifications(tx);
            //Assert.IsNotNull(notifications);
            //Assert.IsTrue(notifications.Count == 1);

            var new_balance = env.token.BalanceOf(env.whitelisted_buyerKeys[n]);

            var token_swap_rate = ImusifyContract.GetTokenSwapRate(ImusifyContract.stage_1_start) / ImusifyContract.imu_decimals;
            var expected_balance = original_balance + neo_amount * (int)token_swap_rate;
            Assert.IsTrue(new_balance == expected_balance);
        }

        [Test]
        public void TestBuyDuringSaleWhitelistedMultiplePurchases()
        {
            var env = new TestEnviroment(1);

            var n = 0;

            var original_balance = env.token.BalanceOf(env.whitelisted_buyerKeys[n]);

            var is_whitelisted = env.IsWhitelisted(env.whitelisted_buyerKeys[n].address.AddressToScriptHash());
            Assert.IsTrue(is_whitelisted);

            env.api.Chain.Time = ImusifyContract.stage_1_start + 100;

            // total should be 10 or less
            var purchases = new int[] { 3, 2, 4, 1 };
            var neo_amount = purchases.Sum();
            env.api.SendAsset(env.owner_keys, env.whitelisted_buyerKeys[n].address, "NEO", neo_amount);

            var total_bough = 0;
            for (int i = 0; i < purchases.Length; i++)
            {
                var tx = TokenSale.MintTokens(env.token, env.whitelisted_buyerKeys[n], "NEO", purchases[i]);
                total_bough += purchases[i];

                Assert.IsNotNull(tx);

                var notifications = env.api.Chain.GetNotifications(tx);
                //Assert.IsNotNull(notifications);
                //Assert.IsTrue(notifications.Count == 1);

                // advance time
                env.api.Chain.Time += (uint)(5 + n % 20);
                Assert.IsTrue(env.api.Chain.Time > ImusifyContract.stage_1_start);
                Assert.IsTrue(env.api.Chain.Time < ImusifyContract.stage_2_start);

                var new_balance = env.token.BalanceOf(env.whitelisted_buyerKeys[n]);
                var token_swap_rate = ImusifyContract.GetTokenSwapRate(ImusifyContract.stage_1_start) / ImusifyContract.imu_decimals;
                var expected_balance = original_balance + total_bough * (int)token_swap_rate;
                Assert.IsTrue(new_balance == expected_balance);
            }
        }


    }
}
