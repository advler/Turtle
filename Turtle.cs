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
        private const int DAYINTERVAL = 20;                     //n日平均
        private const decimal ACCOUNTPERC = 0.01M;              //账户规模的百分比
        private const decimal PERSIZELIMIT = 1000;              //每只证券头寸上限（单位：元）

        private Dictionary<String, TurtleEquity> stocks = new Dictionary<String, TurtleEquity>();      //portfolio corresponding dic

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

            //consolidate one day data
            foreach (var security in Securities)
            {
                var oneDayConsolidator = new TradeBarConsolidator(TimeSpan.FromDays(1));
                oneDayConsolidator.DataConsolidated += OneDayBarHandler;
                SubscriptionManager.AddConsolidator(security.Key, oneDayConsolidator);
            }
        }

        /// <summary>
        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// </summary>
        /// <param name="data">Slice object keyed by symbol containing the stock data</param>
        public override void OnData(Slice data)
        {
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

        //public override void OnOrderEvent(OrderEvent orderEvent)
        //{
        //    var order = Transactions.GetOrderById(orderEvent.OrderId);
        //    Console.WriteLine("Print: {0}: {1}: {2}", Time, order.Type, orderEvent);
        //}

        private void stockSelection()
        {
            stocks.Clear();

            //Add individual stocks.
            stocks.Add("IBM", new TurtleEquity());
            stocks.Add("SPY", new TurtleEquity());

            foreach (String key in stocks.Keys)
            {
                Debug("Stock: " + key + " added to the portfolio.");
                AddEquity(key, Resolution.Minute, Market.USA);
            }
        }

        private void positionSetting(TradeBar data)
        {
            TurtleEquity stk = null;

            if (stocks.TryGetValue(data.Symbol.ToString(), out stk))
            {
                Debug("Calculate daily N for the Symbol:" + data.Symbol + " at: " + Time);

                //get the daily max difference
                decimal tr = Math.Max(data.High - data.Low, data.High - data.Close);
                tr = Math.Max(tr, data.Close - data.Low);
                tr = Math.Max(tr, data.High - data.Open);
                tr = Math.Max(tr, data.Open - data.Low);

                if (stk.PDN >= 0)           //非首日计算头寸
                {
                    //calculate daily N value
                    stk.N = ((DAYINTERVAL - 1)* stk.PDN + tr) / DAYINTERVAL;               
                }
                //非首日计算头寸，to be fixed
                else
                {
                    stk.N = tr;
                }
            } else
            {
                Error("Can not calculate daily N for the Symbol:" + data.Symbol + " at: " + Time);
                stk.PDN = -1;
                stk.Size = -1;
                return;
            }

            //计算头寸规模
            stk.Size = TOTALCASH * ACCOUNTPERC / stk.N;
            stk.PDN = stk.N;
        }

        private void OneDayBarHandler(object sender, TradeBar consolidated)
        {
            Debug("Symbol:" + consolidated.Symbol + " One-day data consolidation at " + Time);

            positionSetting(consolidated);
        }
    }

    public class TurtleEquity
    {
        public decimal PDN { get; set; }            //previous day N value
        public decimal N { get; set; }              //today N value
        public decimal Size { get; set; }           //头寸规模(单位：股)

        public TurtleEquity()
        {
            this.PDN = -1;
            this.Size = -1;
        }
    }
}