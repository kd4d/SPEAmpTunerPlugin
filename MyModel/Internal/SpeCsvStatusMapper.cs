#nullable enable

using System;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>Maps SPE CSV status fields into <see cref="ResponseParser.StatusUpdate"/> for the existing tracker pipeline.</summary>
    internal static class SpeCsvStatusMapper
    {
        public static ResponseParser.StatusUpdate ToStatusUpdate(SpeCsvParseResult csv)
        {
            var u = new ResponseParser.StatusUpdate
            {
                AmpState = csv.AmpState,
                IsPtt = csv.IsPtt,
                ForwardPower = csv.ForwardPower,
                SWR = csv.Swr,
                Voltage = csv.Voltage,
                Current = csv.Current,
                Temperature = csv.Temperature,
                BandNumber = csv.BandNumber,
                BandName = csv.BandName,
                IsVitaDataPopulated = true,
                TunerSWR = csv.Swr
            };

            if (csv.Swr > 0)
                u.ReturnLoss = SWRToReturnLoss(csv.Swr);

            u.FaultCode = DeriveFaultCode(csv.WarningCode, csv.ErrorCode);

            return u;
        }

        private static int DeriveFaultCode(string warn, string err)
        {
            if (string.IsNullOrEmpty(err) || err == "N")
            {
                if (string.IsNullOrEmpty(warn) || warn == "N")
                    return 0;
                return 1;
            }
            return 2;
        }

        private static double SWRToReturnLoss(double swr)
        {
            if (swr <= 1.0) return 99;
            double rho = Math.Abs((swr - 1.0) / (swr + 1.0));
            if (rho <= 0) return 99;
            return Math.Round(-20.0 * Math.Log10(rho), 1);
        }
    }
}
