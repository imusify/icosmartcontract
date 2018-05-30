using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace iMusify
{
    public class ImusifyContract : SmartContract
    {
        // params: 0710
        // return : 05

        public static Object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                // param Owner must be script hash
                bool isOwner = Runtime.CheckWitness(Company_Address);

                if (isOwner)
                {
                    return true;
                }

                // Check if attached assets are accepted
                byte[] sender = GetAssetSender();
                var neo_value = GetContributeValue();
                var purchase_amount = CheckPurchaseAmount(sender, neo_value, false);
                return purchase_amount > 0;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                #region NEP5 METHODS
                if (operation == "totalSupply") return TotalSupply();
                else if (operation == "name") return Name();
                else if (operation == "symbol") return Symbol();

                else if (operation == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];
                    return Transfer(from, to, value);
                }

                else if (operation == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return BalanceOf(account);
                }

                else if (operation == "decimals") return Decimals();
                #endregion

                #region NEP5.1 METHODS
                else if (operation == "allowance")
                {
                    if (args.Length != 2) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    return Allowance(from, to);
                }

                else if (operation == "approve")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];
                    return Approve(from, to, value);
                }

                else if (operation == "transferFrom")
                {
                    if (args.Length != 4) return false;
                    byte[] sender = (byte[])args[0];
                    byte[] from = (byte[])args[1];
                    byte[] to = (byte[])args[2];
                    BigInteger value = (BigInteger)args[3];
                    return TransferFrom(sender, from, to, value);
                }
                #endregion

                #region SALE METHODS
                else if (operation == "deploy") return Deploy();
                else if (operation == "mintTokens") return MintTokens();
                else if (operation == "availableTokens") return AvailableTokens();

                else if (operation == "whitelistCheck")
                {
                    if (args.Length != 1) return false;
                    byte[] address = (byte[])args[0];
                    return WhitelistCheckOne(address);
                }

                else if (operation == "whitelistAdd")
                {
                    if (args.Length == 0) return false;
                    return WhitelistAddFree(args);
                }

                else if (operation == "whitelistRemove")
                {
                    if (args.Length == 0) return false;
                    return WhitelistRemove(args);
                }

                #endregion

                #region VESTING
                else if (operation == "vestingInit")
                {
                    if (args.Length != 5) return false;
                    var targetAdress = (byte[])args[0];
                    var totalAmount = (BigInteger)args[1];
                    var cliffPeriodInDays = (BigInteger)args[2];
                    var trancheCount = (BigInteger)args[3];
                    var paymentPeriodInDays = (BigInteger)args[4];
                    return VestingInit(targetAdress, totalAmount, cliffPeriodInDays, trancheCount, paymentPeriodInDays);
                }

                else if (operation == "vestingDistribute")
                {
                    if (args.Length != 1) return false;
                    var targetAdress = (byte[])args[0];
                    return VestingDistribute(targetAdress);
                }

                else if (operation == "vestingCancel")
                {
                    if (args.Length != 1) return false;
                    var targetAdress = (byte[])args[0];
                    return VestingCancel(targetAdress);
                }

                else if (operation == "vestingAvailablle")
                {
                    if (args.Length != 1) return false;
                    var targetAdress = (byte[])args[0];
                    return VestingAvailable(targetAdress);
                }

                #endregion
            }

            return false;
        }


        #region UTILITY METHODS

        public static bool ValidateAddress(byte[] address)
        {
            if (address.Length != 20)
                return false;
            if (address.AsBigInteger() == 0)
                return false;
            return true;
        }

        #endregion

        #region NEP5
        //Token Settings
        public static string Name() => "imusify Token";
        public static string Symbol() => "IMU";
        public static byte Decimals() => 8;

        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> OnTransferred;

        [DisplayName("refund")]
        public static event Action<byte[], BigInteger> OnRefund;

        [DisplayName("approve")]
        public static event Action<byte[], byte[], BigInteger> OnApproved;

        // get the total token supply
        public static BigInteger TotalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }

        // function that is always called when someone wants to transfer tokens.
        public static bool Transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;
            if (!Runtime.CheckWitness(from)) return false;

            if (!ValidateAddress(to)) return false;

            BigInteger from_value = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
            if (from_value < value) return false;
            if (from == to) return true;

            if (from_value == value)
                Storage.Delete(Storage.CurrentContext, from);
            else
                Storage.Put(Storage.CurrentContext, from, from_value - value);
            BigInteger to_value = Storage.Get(Storage.CurrentContext, to).AsBigInteger();
            Storage.Put(Storage.CurrentContext, to, to_value + value);
            OnTransferred(from, to, value);
            return true;
        }

        public static BigInteger BoughtAmount(byte[] address)
        {
            if (!ValidateAddress(address)) return 0;
            var bought_key = bought_prefix.Concat(address);
            return Storage.Get(Storage.CurrentContext, bought_key).AsBigInteger();
        }

        // Get the account balance of another account with address
        public static BigInteger BalanceOf(byte[] address)
        {
            if (!ValidateAddress(address)) return 0;
            return Storage.Get(Storage.CurrentContext, address).AsBigInteger();
        }

        //
        // NEP5.1 extension methods
        //

        // Transfers tokens from the 'from' address to the 'to' address
        // The Sender must have an allowance from 'From' in order to send it to the 'To'
        // This matches the ERC20 version
        public static bool TransferFrom(byte[] sender, byte[] from, byte[] to, BigInteger value)
        {
            if (!Runtime.CheckWitness(sender)) return false;
            if (!ValidateAddress(from)) return false;
            if (!ValidateAddress(to)) return false;
            if (value <= 0) return false;

            BigInteger from_value = BalanceOf(from);
            if (from_value < value) return false;
            if (from == to) return true;

            // allowance of [from] to [sender]
            byte[] allowance_key = from.Concat(sender);
            BigInteger allowance = Storage.Get(Storage.CurrentContext, allowance_key).AsBigInteger();
            if (allowance < value) return false;

            if (from_value == value)
                Storage.Delete(Storage.CurrentContext, from);
            else
                Storage.Put(Storage.CurrentContext, from, from_value - value);

            if (allowance == value)
                Storage.Delete(Storage.CurrentContext, allowance_key);
            else
                Storage.Put(Storage.CurrentContext, allowance_key, allowance - value);

            // Sender sends tokens to 'To'
            BigInteger to_value = BalanceOf(to);
            Storage.Put(Storage.CurrentContext, to, to_value + value);

            OnTransferred(from, to, value);
            return true;
        }

        // Gives approval to the 'to' address to use amount of tokens from the 'from' address
        // This does not guarantee that the funds will be available later to be used by the 'to' address
        // 'From' is the Tx Sender. Each call overwrites the previous value. This matches the ERC20 version
        public static bool Approve(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;
            if (!Runtime.CheckWitness(from)) return false;
            if (!ValidateAddress(to)) return false;
            if (from == to) return false;

            BigInteger from_value = BalanceOf(from);
            if (from_value < value) return false;

            // overwrite previous value
            byte[] allowance_key = from.Concat(to);
            Storage.Put(Storage.CurrentContext, allowance_key, value);
            OnApproved(from, to, value);
            return true;
        }

        // Gets the amount of tokens allowed by 'from' address to be used by 'to' address
        public static BigInteger Allowance(byte[] from, byte[] to)
        {
            if (!ValidateAddress(from)) return 0;
            if (!ValidateAddress(to)) return 0;
            byte[] allowance_key = from.Concat(to);
            return Storage.Get(Storage.CurrentContext, allowance_key).AsBigInteger();
        }

        #endregion

        #region TOKEN SALE

        public static readonly byte[] Company_Address = "AdgNxYeRq4p7mGAaQMHB2Y7zpAGnxNs4fQ".ToScriptHash();
        public static readonly byte[] Platform_Address = "AGkBYEUo56DtNYaysAowpgd1yKdG6w1SDL".ToScriptHash(); // contains ecoystem + rewards + charity
        public static readonly byte[] Fund_Address = "AXDS5ZtqSQ6R7mkkggsShfr6fGRgYKmirh".ToScriptHash(); // contains private + team + advisors + strategic partnerships supply
                
        public static readonly byte[] Whitelist_Address1 = "AVc5E3bxXzVKCA76zg4DTGZ6LpWi71KrEh".ToScriptHash();
        public static readonly byte[] Whitelist_Address2 = "AYbKZAB1NTNn2dMPTHcPFCCaVgHWG67ZUp".ToScriptHash();
        public static readonly byte[] Whitelist_Address3 = "AbXCAepAQ8swyrui27ZaqQcD8WR5WbRLCA".ToScriptHash();
        public static readonly byte[] Whitelist_Address4 = "ANmTqdidYEeCsFp9qfJLT1gpFmqtVH6PK1".ToScriptHash();

        public static readonly byte[] Whitelist_Address5 = "AL42A8mSfuzmWLYp17j5isyu3PYx4fikHQ".ToScriptHash();
        public static readonly byte[] Whitelist_Address6 = "AFyivKvCYUv1No6PZeG8LVtJVncmnKXkUU".ToScriptHash();
        public static readonly byte[] Whitelist_Address7 = "ASBCKrjiz8FsrUjab3ZGJrFMTnKYSJuzJg".ToScriptHash();
        public static readonly byte[] Whitelist_Address8 = "AV5kaGrhsvuTE1uDjMirKMXxK2bexiF5f4".ToScriptHash();

        public const ulong imu_decimals = 100000000; //decided by Decimals()
        public const ulong neo_decimals = 100000000;

        //ICO Settings
        public static readonly byte[] neo_asset_id = { 155, 124, 255, 218, 166, 116, 190, 174, 15, 147, 14, 190, 96, 133, 175, 144, 147, 229, 254, 86, 179, 74, 92, 34, 12, 205, 207, 110, 252, 51, 111, 197 };

        public const ulong max_supply = 1000000000 * imu_decimals; // total token amount
        public const ulong crowdsale_supply = 460000000 * imu_decimals; // sale1 token amount
        private const ulong presale_supply = 80000000 * imu_decimals;

        public const ulong company_supply = 128000000 * imu_decimals;
        public const ulong ecoystem_supply = 140000000 * imu_decimals;
        public const ulong team_supply = 108000000 * imu_decimals;
        public const ulong partners_supply = 22000000 * imu_decimals;
        public const ulong rewards_supply = 22000000 * imu_decimals;
        public const ulong charity_supply = 80000000 * imu_decimals;
        public const ulong private_supply = 40000000 * imu_decimals;
        public const ulong public_supply = 460000000 * imu_decimals;

        public const ulong platform_supply = ecoystem_supply + rewards_supply + charity_supply;
        public const ulong fund_supply = private_supply + partners_supply + team_supply;

        public const ulong initial_supply = company_supply + ecoystem_supply + team_supply + partners_supply + rewards_supply + charity_supply + private_supply;

        public const ulong token_individual_cap = 9000000 * imu_decimals; // max tokens than an individual can buy from in the crowdsale

        public const uint ico_start_time = 1527379200; // 27 May 00h00 UTC

        [DisplayName("whitelist_add")]
        public static event Action<byte[]> OnWhitelistAdd;

        [DisplayName("whitelist_remove")]
        public static event Action<byte[]> OnWhitelistRemove;

        private static readonly byte[] whitelist_prefix = { (byte)'W', (byte)'L', (byte)'S', (byte)'T' };
        private static readonly byte[] bought_prefix = { (byte)'B', (byte)'G', (byte)'T', (byte)'H' };
        private static readonly byte[] mint_prefix = { (byte)'M', (byte)'I', (byte)'N', (byte)'T' };

        // checks if address is on the whitelist
        public static bool WhitelistCheckOne(byte[] address)
        {
            if (!ValidateAddress(address)) return false;
            var key = whitelist_prefix.Concat(address);
            var val = Storage.Get(Storage.CurrentContext, key).AsBigInteger();
            if (val > 0) return true;
            else return false;
        }

        private static bool IsWhitelistingWitness()
        {
            if (Runtime.CheckWitness(Company_Address))
                return true;
            if (Runtime.CheckWitness(Whitelist_Address1))
                return true;
            if (Runtime.CheckWitness(Whitelist_Address2))
                return true;
            if (Runtime.CheckWitness(Whitelist_Address3))
                return true;
            if (Runtime.CheckWitness(Whitelist_Address4))
                return true;
            if (Runtime.CheckWitness(Whitelist_Address5))
                return true;
            if (Runtime.CheckWitness(Whitelist_Address6))
                return true;
            if (Runtime.CheckWitness(Whitelist_Address7))
                return true;
            if (Runtime.CheckWitness(Whitelist_Address8))
                return true;
            return false;
        }

        // adds addresses to the whitelist
        public static bool WhitelistAddFree(object[] addresses)
        {
            if (!IsWhitelistingWitness())
                return false;

            foreach (var entry in addresses)
            {
                var addressScriptHash = (byte[])entry;
                if (!ValidateAddress(addressScriptHash))
                    continue;

                var key = whitelist_prefix.Concat(addressScriptHash);
                Storage.Put(Storage.CurrentContext, key, 1);
                OnWhitelistAdd(addressScriptHash);

                // balance is max available, so bought tokens = 0
            }

            return true;
        }

        // removes address from the whitelist
        public static bool WhitelistRemove(object[] addresses)
        {
            if (!IsWhitelistingWitness())
                return false;

            foreach (var entry in addresses)
            {
                var addressScriptHash = (byte[])entry;
                if (!ValidateAddress(addressScriptHash))
                    continue;

                var key = whitelist_prefix.Concat(addressScriptHash);
                var val = Storage.Get(Storage.CurrentContext, key).AsBigInteger();
                if (val == 0)
                {
                    continue;
                }

                Storage.Delete(Storage.CurrentContext, key);
                OnWhitelistRemove(addressScriptHash);
            }

            return true;
        }

        // initialization parameters, only once
        public static bool Deploy()
        {
            // Only Team/Admmin/Owner can deploy
            if (!Runtime.CheckWitness(Company_Address))
                return false;

            var current_total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            if (current_total_supply != 0)
            {
                return false;
            }

            OnTransferred(null, Company_Address, company_supply);
            Storage.Put(Storage.CurrentContext, Company_Address, company_supply);

            OnTransferred(null, Platform_Address, platform_supply);
            Storage.Put(Storage.CurrentContext, Platform_Address, platform_supply);

            OnTransferred(null, Fund_Address, fund_supply);
            Storage.Put(Storage.CurrentContext, Fund_Address, fund_supply);

            Storage.Put(Storage.CurrentContext, "totalSupply", initial_supply);

            return true;
        }

        // The function MintTokens is only usable by the chosen wallet
        // contract to mint a number of tokens proportional to the
        // amount of neo sent to the wallet contract. The function
        // can only be called during the tokenswap period
        public static bool MintTokens()
        {
            byte[] sender = GetAssetSender();
            // contribute asset is not neo
            if (sender.Length == 0)
            {
                return false;
            }

            var tx_key = mint_prefix.Concat(sender);
            var last_tx = Storage.Get(Storage.CurrentContext, tx_key);

            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            if (last_tx == tx.Hash)
            {
                return false;
            }

            var contribute_value = GetContributeValue();

            // calculate how many tokens 
            var token_amount = CheckPurchaseAmount(sender, contribute_value, true);
            if (token_amount <= 0)
            {
                return false;
            }

            // mint tokens to sender
            CreditTokensToAddress(sender, token_amount);
            var bought_key = bought_prefix.Concat(sender);
            var total_bought = Storage.Get(Storage.CurrentContext, bought_key).AsBigInteger();
            Storage.Put(Storage.CurrentContext, bought_key, total_bought + token_amount);

            // adjust total supply
            var current_total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            Storage.Put(Storage.CurrentContext, "totalSupply", current_total_supply + token_amount);
            Storage.Put(Storage.CurrentContext, tx_key, tx.Hash);

            return true;
        }

        // returns nuumber of tokens available for sale
        public static BigInteger AvailableTokens()
        {
            BigInteger current_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            BigInteger tokens_available = max_supply - current_supply;
            return tokens_available;
        }

        //  Check how many tokens can be purchased given sender, amount of neo and current conditions
        // only called from a verified context
        private static BigInteger CheckPurchaseAmount(byte[] sender, BigInteger neo_value, bool apply)
        {
            BigInteger tokens_to_refund = 0;
            BigInteger tokens_to_give;


            var cur_time = Runtime.Time;
            var token_swap_rate = GetTokenSwapRate(cur_time);

            if (token_swap_rate == 0)
            {
                // most common case
                if (apply == false)
                    return 0;

                tokens_to_give = 0;
            }
            else
            {
                tokens_to_give = (neo_value / neo_decimals) * token_swap_rate;
            }

            BigInteger current_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            BigInteger tokens_available = max_supply - current_supply;

            var stage_cap = GetTokenStageCap(cur_time);
            if (tokens_available > stage_cap)
            {
                tokens_available = stage_cap;
            }

            // check global hard cap
            if (tokens_to_give > tokens_available)
            {
                tokens_to_refund = (tokens_to_give - tokens_available);
                tokens_to_give = tokens_available;
            }

            var bought_key = bought_prefix.Concat(sender);
            var total_bought = Storage.Get(Storage.CurrentContext, bought_key).AsBigInteger();

            var whitelist_key = whitelist_prefix.Concat(sender);
            var whitelist_entry = Storage.Get(Storage.CurrentContext, whitelist_key).AsBigInteger();
            if (whitelist_entry <= 0) // not whitelisted
            {
                tokens_to_refund += tokens_to_give;
                tokens_to_give = 0;
            }
            else
            {
                var new_balance = tokens_to_give + total_bought;

                // check individual cap
                if (new_balance > token_individual_cap)
                {
                    var diff = (new_balance - token_individual_cap);
                    tokens_to_refund += diff;
                    tokens_to_give -= diff;
                }
            }

            if (apply)
            {
                // here we do partial refunds only, full refunds are done in verification trigger!
                if (tokens_to_refund > 0 && tokens_to_give > 0)
                {
                    // convert amount to NEO
                    OnRefund(sender, (tokens_to_refund / token_swap_rate) * neo_decimals);
                }
            }

            return tokens_to_give;
        }

        // check whether asset is neo and get sender script hash
        private static byte[] GetAssetSender()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] reference = tx.GetReferences();
            var receiver = GetReceiver();
            foreach (TransactionOutput output in reference)
            {
                if (output.ScriptHash != receiver && output.AssetId == neo_asset_id)
                {
                    return output.ScriptHash;
                }
            }
            return new byte[] { };
        }

        // get smart contract script hash
        private static byte[] GetReceiver()
        {
            return ExecutionEngine.ExecutingScriptHash;
        }

        // get all you contribute neo amount
        private static BigInteger GetContributeValue()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            var receiver = GetReceiver();

            TransactionOutput[] inputs = tx.GetReferences();
            foreach (var input in inputs)
            {
                if (input.ScriptHash == receiver)
                {
                    return 0;
                }
            }

            TransactionOutput[] outputs = tx.GetOutputs();
            BigInteger value = 0;

            // get the total amount of Neo
            foreach (var output in outputs)
            {
                if (output.ScriptHash == receiver && output.AssetId == neo_asset_id)
                {
                    value += output.Value;
                }
            }
            return value;
        }

        // only called from a verified context
        private static void CreditTokensToAddress(byte[] addressScriptHash, BigInteger amount)
        {
            var balance = Storage.Get(Storage.CurrentContext, addressScriptHash).AsBigInteger();
            Storage.Put(Storage.CurrentContext, addressScriptHash, amount + balance);
            OnTransferred(null, addressScriptHash, amount);
        }

        public const ulong stage_1_start = 1527818400;
        public const ulong stage_2_start = 1530410400;
        public const ulong stage_3_start = 1530576120;
        public const ulong stage_4_start = 1531188000;
        public const ulong stage_5_start = 1531792800;
        public const ulong stage_6_start = 1532397600;
        public const ulong sale_end = 1533002399;

        // how many tokens you get per NEO
        private static BigInteger GetTokenSwapRate(uint timestamp)
        {
            if (timestamp > sale_end)
            {
                return 0;
            }

            if (timestamp >= stage_6_start)
            {
                return 1000 * imu_decimals;
            }

            if (timestamp >= stage_5_start)
            {
                return 1030 * imu_decimals;
            }

            if (timestamp >= stage_4_start)
            {
                return 1065 * imu_decimals;
            }

            if (timestamp >= stage_3_start)
            {
                return 1100 * imu_decimals;
            }

            if (timestamp >= stage_2_start)
            {
                return 1115 * imu_decimals;
            }

            if (timestamp >= stage_1_start)
            {
                return 1430 * imu_decimals;
            }

            return 0;
        }

        // how many tokens available to each stage
        private static BigInteger GetTokenStageCap(uint timestamp)
        {
            if (timestamp >= stage_2_start)
            {
                return crowdsale_supply;
            }

            if (timestamp >= stage_1_start)
            {
                return presale_supply;
            }

            return 0;
        }

        #endregion

        #region VESTING
        private static readonly byte[] vesting_total_prefix = { (byte)'V', (byte)'S', (byte)'T', (byte)'T' };
        private static readonly byte[] vesting_cliff_prefix = { (byte)'V', (byte)'S', (byte)'T', (byte)'C' };
        private static readonly byte[] vesting_tranche_prefix = { (byte)'V', (byte)'S', (byte)'T', (byte)'R' };
        private static readonly byte[] vesting_period_prefix = { (byte)'V', (byte)'S', (byte)'T', (byte)'P' };
        private static readonly byte[] vesting_unlock_prefix = { (byte)'V', (byte)'S', (byte)'T', (byte)'U' };
        private static readonly byte[] vesting_start_prefix = { (byte)'V', (byte)'S', (byte)'T', (byte)'S' };

        public static bool VestingInit(byte[] targetAddress, BigInteger totalAmount, BigInteger cliffPeriodInDays, BigInteger trancheCount, BigInteger paymentPeriodInDays)
        {
            if (!ValidateAddress(targetAddress))
            {
                return false;
            }

            if (targetAddress == Fund_Address) {
                return false;
            }

            if (!Runtime.CheckWitness(Company_Address))
            {
                return false;
            }

            // minimum cliff = 48 hours
            if (cliffPeriodInDays < 2)
            {
                return false;
            }

            var total_key = vesting_total_prefix.Concat(targetAddress);
            var current_total = Storage.Get(Storage.CurrentContext, total_key).AsBigInteger();
            if (current_total > 0)
            {
                return false;
            }

            Storage.Put(Storage.CurrentContext, total_key, totalAmount);

            var cliff_key = vesting_cliff_prefix.Concat(targetAddress);
            Storage.Put(Storage.CurrentContext, cliff_key, cliffPeriodInDays);

            var tranche_key = vesting_tranche_prefix.Concat(targetAddress);
            Storage.Put(Storage.CurrentContext, tranche_key, trancheCount);

            var period_key = vesting_period_prefix.Concat(targetAddress);
            Storage.Put(Storage.CurrentContext, period_key, paymentPeriodInDays);

            var start_key = vesting_start_prefix.Concat(targetAddress);
            Storage.Put(Storage.CurrentContext, start_key, Runtime.Time);

            var unlock_key = vesting_unlock_prefix.Concat(targetAddress);
            BigInteger unlock_count = 0;
            Storage.Put(Storage.CurrentContext, unlock_key, unlock_count);

            BigInteger fund_balance = Storage.Get(Storage.CurrentContext, Fund_Address).AsBigInteger();
            if (fund_balance < totalAmount) return false;

            Storage.Put(Storage.CurrentContext, Fund_Address, fund_balance - totalAmount);

            OnTransferred(Fund_Address, null, totalAmount);

            return true;
        }

        // calculates how many tokens are available to unlock
        public static BigInteger VestingAvailable(byte[] targetAddress)
        {
            if (!ValidateAddress(targetAddress))
            {
                return 0;
            }

            if (targetAddress == Fund_Address)
            {
                return 0;
            }

            var total_key = vesting_total_prefix.Concat(targetAddress);
            var vested_total = Storage.Get(Storage.CurrentContext, total_key).AsBigInteger();
            if (vested_total == 0)
            {
                return 0;
            }

            var start_key = vesting_start_prefix.Concat(targetAddress);
            var start_time = Storage.Get(Storage.CurrentContext, start_key).AsBigInteger();

            var total_days = (Runtime.Time - start_time) / 86400; // divide by seconds in a day

            var cliff_key = vesting_cliff_prefix.Concat(targetAddress);
            var cliffDays = Storage.Get(Storage.CurrentContext, cliff_key).AsBigInteger();

            if (total_days < cliffDays)
            {
                return 0;
            }

            var period_key = vesting_period_prefix.Concat(targetAddress);
            var periods = Storage.Get(Storage.CurrentContext, period_key).AsBigInteger();

            var tranches_available = (total_days - cliffDays) / periods;

            var unlock_key = vesting_unlock_prefix.Concat(targetAddress);
            var unlock_count = Storage.Get(Storage.CurrentContext, unlock_key).AsBigInteger();

            // subtract number of tranches already unlocked
            tranches_available = tranches_available - unlock_count;
            if (tranches_available <= 0)
            {
                return 0;
            }

            var tranche_key = vesting_tranche_prefix.Concat(targetAddress);
            var totalTranches = Storage.Get(Storage.CurrentContext, tranche_key).AsBigInteger();

            var amountPerTranche = (vested_total / totalTranches);
            var total_available = amountPerTranche * tranches_available;
            return total_available;
        }

        public static bool VestingDistribute(byte[] targetAddress)
        {
            if (!Runtime.CheckWitness(Company_Address))
            {
                return false;
            }

            if (!ValidateAddress(targetAddress))
            {
                return false;
            }

            if (targetAddress == Fund_Address)
            {
                return false;
            }

            var total_key = vesting_total_prefix.Concat(targetAddress);
            var vested_total = Storage.Get(Storage.CurrentContext, total_key).AsBigInteger();
            if (vested_total == 0)
            {
                return false;
            }

            var start_key = vesting_start_prefix.Concat(targetAddress);
            var start_time = Storage.Get(Storage.CurrentContext, start_key).AsBigInteger();

            var total_days = (Runtime.Time - start_time) / 86400; // divide by seconds in a day

            var cliff_key = vesting_cliff_prefix.Concat(targetAddress);
            var cliffDays = Storage.Get(Storage.CurrentContext, cliff_key).AsBigInteger();

            if (total_days < cliffDays)
            {
                return false;
            }

            var period_key = vesting_period_prefix.Concat(targetAddress);
            var periods = Storage.Get(Storage.CurrentContext, period_key).AsBigInteger();

            var tranches_available = (total_days - cliffDays) / periods;

            var unlock_key = vesting_unlock_prefix.Concat(targetAddress);
            var unlock_count = Storage.Get(Storage.CurrentContext, unlock_key).AsBigInteger();

            // subtract number of tranches already unlocked
            tranches_available = tranches_available - unlock_count;
            if (tranches_available <= 0)
            {
                return false;
            }

            var tranche_key = vesting_tranche_prefix.Concat(targetAddress);
            var totalTranches = Storage.Get(Storage.CurrentContext, tranche_key).AsBigInteger();

            var amountPerTranche = (vested_total / totalTranches);
            var total_available = amountPerTranche * tranches_available;

            if (total_available <= 0)
            {
                return false;
            }

            unlock_count += tranches_available;
            Storage.Put(Storage.CurrentContext, unlock_key, unlock_count);

            BigInteger target_balance = Storage.Get(Storage.CurrentContext, targetAddress).AsBigInteger();
            target_balance += total_available;

            Storage.Put(Storage.CurrentContext, targetAddress, target_balance);

            OnTransferred(null, targetAddress, total_available);

            return true;
        }

        public static bool VestingCancel(byte[] targetAddress)
        {
            if (!Runtime.CheckWitness(Company_Address))
            {
                return false;
            }

            if (!ValidateAddress(targetAddress))
            {
                return false;
            }

            var start_key = vesting_start_prefix.Concat(targetAddress);
            var start_time = Storage.Get(Storage.CurrentContext, start_key).AsBigInteger();

            var total_days = (Runtime.Time - start_time) / 86400; // divide by seconds in a day

            if (total_days > 1)
            {
                return false;
            }

            Storage.Delete(Storage.CurrentContext, start_key);

            var cliff_key = vesting_cliff_prefix.Concat(targetAddress);
            Storage.Delete(Storage.CurrentContext, cliff_key);

            var tranche_key = vesting_tranche_prefix.Concat(targetAddress);
            Storage.Delete(Storage.CurrentContext, tranche_key);

            var period_key = vesting_period_prefix.Concat(targetAddress);
            Storage.Delete(Storage.CurrentContext, period_key);

            var unlock_key = vesting_unlock_prefix.Concat(targetAddress);
            Storage.Delete(Storage.CurrentContext, unlock_key);

            var total_key = vesting_total_prefix.Concat(targetAddress);
            var vested_total = Storage.Get(Storage.CurrentContext, total_key).AsBigInteger();
            Storage.Delete(Storage.CurrentContext, total_key);

            BigInteger fund_balance = Storage.Get(Storage.CurrentContext, Fund_Address).AsBigInteger();
            fund_balance += vested_total;

            Storage.Put(Storage.CurrentContext, Fund_Address, fund_balance);

            OnTransferred(null, Fund_Address, vested_total);

            return true;
        }

        #endregion

    }
}
