/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using QuantConnect.Data;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;
using QuantConnect.Securities.Equity;
using QuantConnect.Securities;
using QuantConnect.Indicators;
using QuantConnect.Orders;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// Basic template algorithm simply initializes the date range and cash. This is a skeleton
    /// framework you can use for designing an algorithm.
    /// </summary>
    /// <meta name="tag" content="using data" />
    /// <meta name="tag" content="using quantconnect" />
    /// <meta name="tag" content="trading and orders" />
    public class Turtle : QCAlgorithm
    {
        //const values
        private const decimal TOTALCASH = 10000;                //总资金
        private const int NUMDAYAVG = 1;                        //n日取平均值
        private const int NUMDAYWU = 4;                         //warming up days
        private const int NUMDAYMAX = 3;                        //n日取最大值
        private const decimal ACCOUNTPERC = 0.01M;              //账户规模的百分比
        private const decimal PERSIZELIMIT = 1000;              //每只证券头寸上限（单位：元）

        private readonly Dictionary<Symbol, SymbolData> _sd = new Dictionary<Symbol, SymbolData>();      //portfolio corresponding dic

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        public override void Initialize()
        {
            //set trade period
            SetStartDate(2013, 10, 10);  //Set Start Date
            SetEndDate(2013, 10, 11);    //Set End Date

            //设置总资金
            SetCash(TOTALCASH);             //Set Strategy Cash

            //select stocks to be traded.
            stockSelection();

            foreach (var val in _sd.Values)
            {
                Schedule.On(DateRules.EveryDay(val.Symbol), TimeRules.AfterMarketOpen(val.Symbol, -1), () =>
                {
                    Debug("EveryDay." + val.Symbol.ToString() + " initialize at: " + Time);
                    Transactions.CancelOpenOrders(val.Symbol);
                    val.Dailylastsettledprice = -1;
                });
            }
            
            SetWarmup(TimeSpan.FromDays(NUMDAYWU));
        }

        /// <summary>
        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// </summary>
        /// <param name="data">Slice object keyed by symbol containing the stock data</param>
        public override void OnData(Slice data)
        {
            // we are only using warmup for indicator spooling, so wait for us to be warm then continue
            if (IsWarmingUp) return;

            foreach (var sd in _sd.Values)
            {
                if (sd.IsReady)
                {
                    var lastPriceTime = sd.Close.Current.Time;
                    // only make decisions when we have data on our requested resolution
                    if (lastPriceTime.RoundDown(sd.Security.Resolution.ToTimeSpan()) == lastPriceTime)
                    {
                        sd.Update();
                    }
                } else
                {
                    continue;
                }
            }

            //SetHoldings example
            //if (!Portfolio.Invested)
            //{
            //    SetHoldings(_spy, 1);
            //    Debug("Purchased Stock");
            //    Debug("Remain cash: " + Portfolio.Cash);
            //}

            ////Creating an Order example
            //if (!Portfolio.Invested)
            //{
            //    var bar = data[_ibm];
            //    LimitOrder(_ibm, 10, bar.High + 0.1m);
            //    Debug("Purchased stock at time: " + bar.Time);
            //    Debug("Remain cash: " + Portfolio.Cash);
            //}
        }

        public override void OnOrderEvent(OrderEvent fill)
        {
            SymbolData sd;
            if (_sd.TryGetValue(fill.Symbol, out sd))
            {
                sd.OnOrderEvent(fill);
            }
        }

        private void stockSelection()
        {
            _sd.Clear();

            //Add individual stocks.
            AddEquity("SPY", Resolution.Minute, Market.USA);
            AddEquity("IBM", Resolution.Minute, Market.USA);

            foreach (var security in Securities)
            {
                _sd.Add(security.Key, new SymbolData(security.Key, this));
            }
        }

        //set position for each stock
        private void positionSetting(Symbol symbol, IndicatorBase<IndicatorDataPoint> data)
        {
            SymbolData sd = null;

            if (_sd.TryGetValue(symbol, out sd))
            {
                Debug("Calculate position value for the Symbol:" + symbol.ToString() + " at: " + Time);
                sd.Position = (int)(TOTALCASH * ACCOUNTPERC / data);
            }
            else
            {
                Error("Can not calculate position value for the Symbol:" + symbol.ToString() + " at: " + Time);
                sd.Position = 0;
            }
        }

        public ExponentialMovingAverage EMA(Symbol symbol, int period, TimeSpan interval,
            Func<TradeBar, decimal> selector = null)
        {
            var ema = new ExponentialMovingAverage(symbol.ToString() + "_EMA_" + period + "_" + interval.ToString(), period);

            // Calculate the daily actual max price difference
            selector = selector ?? (x => Math.Max((Math.Max((Math.Max((Math.Max(x.High - x.Low,
                x.High - x.Close)), x.Close - x.Low)), x.High - x.Open)), x.Open - x.Low));

            RegisterIndicator(symbol, ema, interval, selector);
            return ema;
        }

        public Maximum MAX(Symbol symbol, int period, TimeSpan interval, Func<TradeBar, decimal> selector = null)
        {
            var max = new Maximum(symbol.ToString() + "_MAX_" + period + "_" + interval.ToString(), period);

            // assign a default value for the selector function
            if (selector == null)
            {
                selector = x => x.High;
            }

            RegisterIndicator(symbol, max, interval, selector);
            return max;
        }

        public void RegisterIndicator(Symbol symbol, IndicatorBase<IndicatorDataPoint> indicator,
            TimeSpan interval, Func<TradeBar, decimal> selector = null)
        {
            selector = selector ?? (x => x.Value);

            var consolidator = new TradeBarConsolidator(interval);

            // register the consolidator for automatic updates via SubscriptionManager
            SubscriptionManager.AddConsolidator(symbol, consolidator);

            // attach to the DataConsolidated event so it updates our indicator
            consolidator.DataConsolidated += (sender, consolidated) =>
            {
                var value = selector(consolidated);
                indicator.Update(new IndicatorDataPoint(consolidated.Symbol, consolidated.EndTime, value));
                positionSetting(symbol, indicator);
            };
        }

        class SymbolData
        {
            public readonly Symbol Symbol;
            public readonly Security Security;

            public decimal Quantity
            {
                get { return Security.Holdings.Quantity; }
            }

            public readonly Identity Close;
            public readonly ExponentialMovingAverage EMA;
            public readonly Maximum MAX;

            private readonly Turtle _algorithm;

            public int Position { get; set; }                            //持仓上限（单位股）
            public decimal Dailylastsettledprice { get; set; }               //当日最近成交价

            public SymbolData(Symbol symbol, Turtle algorithm)
            {
                Symbol = symbol;
                Security = algorithm.Securities[symbol];
                Position = 0;
                Dailylastsettledprice = -1;

                Close = algorithm.Identity(symbol);
                EMA = algorithm.EMA(symbol, NUMDAYAVG, TimeSpan.FromDays(1));
                MAX = algorithm.MAX(symbol, NUMDAYMAX, TimeSpan.FromDays(1));

                // if we're receiving daily

                _algorithm = algorithm;
            }

            public bool IsReady
            {
                get { return Close.IsReady && MAX.IsReady & EMA.IsReady; }
            }

            public void Update()
            {
                OrderTicket ticket;
                TryEnter(out ticket);
                //TryExit(out ticket);
            }

            public bool TryEnter(out OrderTicket ticket)
            {
                ticket = null;

                //if we reach the position limit then exit
                int hardlimit = (int)(PERSIZELIMIT / Security.High);
                if (Security.Holdings.Quantity >= hardlimit ||
                    Security.Holdings.Quantity >= Position)
                {
                    return false;
                }

                if (Dailylastsettledprice < 0 && _algorithm.Transactions.GetOpenOrders(Symbol).Count == 0)          
                //haven't made any trade current day
                {
                    if (Security.Low >= MAX)
                    {
                        ticket = _algorithm.LimitOrder(Symbol, 1, Security.Low, "TryEnter at: " + Security.Low);
                        return true;
                    }
                } else if (Dailylastsettledprice > 0 && _algorithm.Transactions.GetOpenOrders(Symbol).Count == 0)
                {
                    if (Security.Low >= Dailylastsettledprice + EMA / 2)
                    {
                        ticket = _algorithm.LimitOrder(Symbol, 1, Security.Low, "TryEnter at: " + Security.Low);
                        return true;
                    }
                }

                return false;
            }

            //public bool TryExit(out OrderTicket ticket)
            //{
            //    const decimal exitTolerance = 1 + 2 * PercentTolerance;

            //    ticket = null;
            //    if (!Security.Invested)
            //    {
            //        // can't exit if we haven't entered
            //        return false;
            //    }

            //    decimal limit = 0m;
            //    if (Security.Holdings.IsLong && Close * exitTolerance < EMA)
            //    {
            //        limit = Security.High;
            //    }
            //    else if (Security.Holdings.IsShort && Close > EMA * exitTolerance)
            //    {
            //        limit = Security.Low;
            //    }
            //    if (limit != 0)
            //    {
            //        ticket = _algorithm.LimitOrder(Symbol, -Quantity, limit, "TryExit at: "
            //            + limit + " at: " + _algorithm.Time);
            //    }
            //    return -Quantity != 0;
            //}

            public void OnOrderEvent(OrderEvent fill)
            {
                if (fill.Status == OrderStatus.Invalid)
                {
                    _algorithm.Debug(fill.Symbol.ToString() + "'s order: " +
                        fill.OrderId + " is invalid at: " + _algorithm.Time);
                    return;
                }
                if (fill.Status == OrderStatus.Filled)
                {
                    Dailylastsettledprice = fill.FillPrice;
                    _algorithm.Debug(fill.Symbol.ToString() + "'s order: " +
                        fill.OrderId + " is filled at: " + _algorithm.Time);
                    return;
                }
            }
        }
    }
}