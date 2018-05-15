using System;
using QuantConnect.Securities.Equity;

namespace QuantConnect.Algorithm.CSharp
{
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
