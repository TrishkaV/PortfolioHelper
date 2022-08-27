namespace PortfolioHelper
{
    internal sealed class Alarm
    {
        internal string ticker { get; }
        /* "target" can either be a double (price level) or an indicator */
        internal double? targetPrice { get; set; }
        internal string? targetIndicator { get; set; }
        /* "true" is a buy order while "false" is a sell */
        internal bool direction { get; set; }
        internal double? capitalToInvest { get; set; }

        /* used for alarms to disable */
        internal Alarm(string ticker, dynamic target)
        {
            this.ticker = ticker;
            var isTargetAssignOK = AssignTarget(target);
        }

        internal Alarm(string ticker, dynamic target, bool direction, dynamic? capitalToInvest)
        {
            this.ticker = ticker;
            this.direction = direction;
            var isTargetAssignOK = AssignTarget(target);
            var isCapToInvestAssignOK = AssignCapitalToInvest(capitalToInvest);
        }

        private bool AssignCapitalToInvest(dynamic capitalToInvest)
        {
            this.capitalToInvest = capitalToInvest switch
            {
                null => capitalToInvest,
                _ => capitalToInvest.GetType().Name switch
                {
                    "Double" => capitalToInvest,
                    "String" when double.TryParse(capitalToInvest, out double x) => x,
                    _ => null
                }
            };

            return true;
        }

        private bool AssignTarget(dynamic target)
        {
            this.targetPrice = target.GetType().Name switch
            {
                "Double" => target,
                "String" => double.TryParse(target, out double x) switch
                {
                    true => x,
                    _ => null
                },
                _ => -1d
            };
            if (this.targetPrice == -1d)
            {
                SysUtils.LogErrAndNotify($"This exception should never happen, \"targetType\": \"{target.GetType().Name}\" is not valid, please use a \"double\" or a \"string\".");
                return false;
            }

            if (this.targetPrice == null)
                this.targetIndicator = target;

            return true;
        }
    }
}
