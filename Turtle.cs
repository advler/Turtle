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
        private const int NUMDAYAVG = 20;                        //n日取平均值
        private const int NUMDAYWU = 56;                         //warming up days
        private const int NUMDAYMAX = 55;                        //n日取最大值
        private const int NUMDAYMIN = 20;                        //n日取最小值
        private const decimal ACCOUNTPERC = 0.01M;              //账户规模的百分比（用于确定每股头寸规模）
        private const decimal PERSIZELIMIT = 1000;              //每只证券头寸上限（单位：元）

        private readonly Dictionary<Symbol, SymbolData> _sd = new Dictionary<Symbol, SymbolData>();      //portfolio corresponding dic

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and
        /// start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        public override void Initialize()
        {
            //set trade period
            SetStartDate(2010, 01, 01);  //Set Start Date
            SetEndDate(2018, 05, 01);    //Set End Date

            //设置总资金
            SetCash(TOTALCASH);             //Set Strategy Cash

            //select stocks to be traded.
            stockSelection();

            foreach (var val in _sd.Values)
            {
                Schedule.On(DateRules.EveryDay(val.Symbol), TimeRules.AfterMarketOpen(val.Symbol, -1), () =>
                {
                    Debug("EveryDay." + val.Symbol.ToString() + " initialize at: " + Time);
                    Transactions.CancelOpenOrders(val.Symbol);                  //close all open orders at the daily beginning
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
            AddEquity("AAPL", Resolution.Second, Market.USA);
            AddEquity("IBM", Resolution.Second, Market.USA);
            AddEquity("INTC", Resolution.Second, Market.USA);

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

        public Minimum MIN(Symbol symbol, int period, TimeSpan interval, Func<TradeBar, decimal> selector = null)
        {
            var min = new Minimum(symbol.ToString() + "_MIN_" + period + "_" + interval.ToString(), period);

            // assign a default value for the selector function
            if (selector == null)
            {
                selector = x => x.Low;
            }

            RegisterIndicator(symbol, min, interval, selector);
            return min;
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
            public readonly Minimum MIN;

            private readonly Turtle _algorithm;

            public int Position { get; set; }                            //持仓上限（单位股）
            public decimal LastFillPrice { get; set; }               //最近成交价

            public SymbolData(Symbol symbol, Turtle algorithm)
            {
                Symbol = symbol;
                Security = algorithm.Securities[symbol];
                Position = 0;
                LastFillPrice = -1;

                Close = algorithm.Identity(symbol);
                EMA = algorithm.EMA(symbol, NUMDAYAVG, TimeSpan.FromDays(1));
                MAX = algorithm.MAX(symbol, NUMDAYMAX, TimeSpan.FromDays(1));
                MIN = algorithm.MIN(symbol, NUMDAYMIN, TimeSpan.FromDays(1));

                _algorithm = algorithm;
            }

            public bool IsReady
            {
                get { return Close.IsReady && MAX.IsReady & EMA.IsReady; }
            }

            public void Update()
            {
                //reset LastFillPrice
                if ((int)(Security.Holdings.Quantity) == 0)
                    LastFillPrice = -1;

                OrderTicket ticket;                             //enter ticket
                List<int> idlist;                                //force-quit id list

                TryForceQuit(out idlist);                   //止损
                TryExit(out idlist);                        //退出
                TryEnter(out ticket);                       //入市
            }

            public void TryEnter(out OrderTicket ticket)
            {
                ticket = null;

                //if we reach the position limit then exit
                int hardlimit = (int)(PERSIZELIMIT / Security.High);
                if (Security.Holdings.Quantity >= hardlimit ||
                    Security.Holdings.Quantity >= Position)
                {
                    return;
                }

                if (LastFillPrice < 0 && _algorithm.Transactions.GetOpenOrders(Symbol).Count == 0)
                //haven't made any trade current day
                {
                    if (Security.Low >= MAX)
                    {
                        ticket = _algorithm.LimitOrder(Symbol, 1, Security.High, "TryEnter at: " + Security.Low);
                        _algorithm.Debug("Enter one ticket for the symbol: "
                            + Symbol.ToString() + " at: " + _algorithm.Time);
                    }
                } else if (LastFillPrice > 0 && _algorithm.Transactions.GetOpenOrders(Symbol).Count == 0)
                {
                    if (Security.Low >= LastFillPrice + EMA / 2)
                    {
                        ticket = _algorithm.LimitOrder(Symbol, 1, Security.High, "TryEnter at: " + Security.Low);
                        _algorithm.Debug("Enter one ticket for the symbol: "
                            + Symbol.ToString() + " at: " + _algorithm.Time);
                    }
                }
            }

            //退出
            public void TryExit(out List<int> idlist)
            {
                idlist = null;

                if (Security.High < MIN)
                {
                    idlist = _algorithm.Liquidate(Symbol, "TryExit");  //Liquidate all holdings
                    LastFillPrice = -1;                     //reset LastFillPrice
                    _algorithm.Debug("exit for the symbol: "
                            + Symbol.ToString() + " at: " + _algorithm.Time);
                }
            }

            //止损
            public void TryForceQuit(out List<int> idlist)
            {
                idlist = null;

                if (LastFillPrice - Security.High >= 2 * EMA)
                {
                    idlist = _algorithm.Liquidate(Symbol, "TryForceQuit");  //Liquidate all holdings
                    LastFillPrice = -1;                     //reset LastFillPrice
                    _algorithm.Debug("Force quit for the symbol: "
                            + Symbol.ToString() + " at: " + _algorithm.Time);
                }
            }

            public void OnOrderEvent(OrderEvent fill)
            {
                if (fill.Status == OrderStatus.Invalid)
                {
                    _algorithm.Debug(fill.Symbol.ToString() + "'s order: " +
                        fill.OrderId + " is invalid at: " + _algorithm.Time);
                    return;
                }
                if (fill.Status == OrderStatus.Filled && fill.FillQuantity > 0)
                {
                    LastFillPrice = fill.FillPrice;
                    _algorithm.Debug(fill.Symbol.ToString() + "'s order: " +
                        fill.OrderId + " is filled at: " + _algorithm.Time);
                    return;
                }
            }
        }
    }
}