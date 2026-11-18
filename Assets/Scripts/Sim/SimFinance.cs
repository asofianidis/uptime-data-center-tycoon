using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCT
{
    /// <summary>
    /// Financing and risk: term loans, a revolving credit line, the credit score that prices them, and
    /// property insurance that pays out on fires, floods, leaks and theft.
    /// </summary>
    public partial class Simulation
    {
        // ================================================================ credit score
        static readonly string[] Lenders = { "First Silicon Bank", "Harbor Credit Union", "Granite Capital", "Meridian Trust" };

        public float TotalDebt => S.loans.Sum(l => l.balance);
        public Loan CreditLine => S.loans.FirstOrDefault(l => l.creditLine);
        public float MonthlyDebtService => S.loans.Where(l => !l.creditLine).Sum(l => l.payment) + (CreditLine != null ? CreditLine.balance * CreditLine.rate / 12f : 0f);

        public static string CreditBand(float score) => score >= 760 ? "Excellent" : score >= 700 ? "Good" : score >= 640 ? "Fair" : score >= 580 ? "Weak" : "Poor";

        /// <summary>Where the score is heading, with the reasons (shown in the Loans tab).</summary>
        public float CreditTarget(List<string> why)
        {
            float t = 600;
            var active = S.contracts.Where(c => c.status == ContractStatus.Active).ToList();
            if (active.Count > 0)
            {
                float up = active.Average(c => Uptime(c, false));
                float u = Mathf.Clamp((up - 99f) * 60f, -40f, 60f);
                t += u;
                why?.Add($"{(u >= 0 ? "+" : "")}{u:0} customer uptime {up:0.00}%");
            }
            var recent = S.reports.Skip(Mathf.Max(0, S.reports.Count - 3)).ToList();
            if (recent.Count > 0)
            {
                float op = recent.Average(r => r.Operating);
                float p = Mathf.Clamp(op / 2500f, -70f, 70f);
                t += p;
                why?.Add($"{(p >= 0 ? "+" : "")}{p:0} operating profit (3-month average {K0(op)})");
            }
            float revenue = Mathf.Max(1f, MonthlyRevenue * 12f);
            float ratio = TotalDebt / revenue;
            if (TotalDebt > 0)
            {
                float d = -Mathf.Clamp(ratio * 90f, 0f, 140f);
                t += d;
                why?.Add($"{d:0} debt is {ratio * 100:0}% of annual revenue");
            }
            if (S.missedPayments > 0) { t -= S.missedPayments * 35f; why?.Add($"-{S.missedPayments * 35} missed payments ({S.missedPayments})"); }
            if (S.loansRepaid > 0) { float b = Mathf.Min(60, S.loansRepaid * 15); t += b; why?.Add($"+{b:0} loans repaid in full ({S.loansRepaid})"); }
            t += S.Level * 6; why?.Add($"+{S.Level * 6} reputation level {S.Level}");
            if (S.ipo) { t += 30; why?.Add("+30 publicly listed"); }
            return Mathf.Clamp(t, 300f, 850f);
        }

        static string K0(float v) => (v < 0 ? "-$" : "$") + Mathf.Abs(v).ToString("N0");

        void MonthlyCredit()
        {
            S.creditScore = Mathf.Clamp(S.creditScore + (CreditTarget(null) - S.creditScore) * 0.4f, 300f, 850f);
            // Old black marks fade over time.
            if (S.missedPayments > 0 && S.Month % 6 == 0) S.missedPayments--;
            S.claimsRecent = Mathf.Max(0f, S.claimsRecent - 0.1f);
        }

        // ================================================================ loans
        /// <summary>Annual interest rate offered for a term loan at the current credit score.</summary>
        public float LoanRate(int months)
        {
            float r = 0.045f + (850f - S.creditScore) / 550f * 0.14f;
            r += months >= 48 ? 0.012f : months >= 24 ? 0.005f : 0f;
            return Mathf.Round(r * 1000f) / 1000f;
        }

        /// <summary>Total the bank will lend (all loans, including the credit line).</summary>
        public float BorrowingLimit
        {
            get
            {
                float assets = S.sites.Sum(site => site.objects.Sum(o => Catalog.Facility(o.def)?.cost ?? 0) * 0.35f);
                float basis = 250000f + MonthlyRevenue * 12f + assets * 1.4f;
                float scoreMul = Mathf.Clamp01((S.creditScore - 400f) / 350f);
                return Mathf.Round(basis * scoreMul / 10000f) * 10000f;
            }
        }

        public float LoanHeadroom => Mathf.Max(0, BorrowingLimit - TotalDebt);

        public static float Payment(float principal, float annualRate, int months)
        {
            float r = annualRate / 12f;
            if (r <= 0) return principal / months;
            return principal * r / (1f - Mathf.Pow(1f + r, -months));
        }

        public string TakeLoan(float amount, int months)
        {
            if (amount <= 0) return "Choose an amount";
            if (S.creditScore < 480) return "Your credit score is too low for new loans";
            if (amount > LoanHeadroom + 1f) return $"The bank will lend at most {K0(LoanHeadroom)} more";
            float rate = LoanRate(months);
            var l = new Loan
            {
                id = S.NewId(),
                lender = Lenders[S.loans.Count % Lenders.Length],
                principal = amount,
                balance = amount,
                rate = rate,
                months = months,
                monthsLeft = months,
                payment = Mathf.Round(Payment(amount, rate, months)),
                nextHour = S.hour + Catalog.HoursPerMonth,
            };
            S.loans.Add(l);
            S.money += amount;
            S.current.borrowed += amount;
            Log($"Borrowed {K0(amount)} from {l.lender} over {months} months at {rate * 100:0.0}% ({K0(l.payment)}/month).", 1);
            return null;
        }

        public float CreditLineLimit => Mathf.Round(BorrowingLimit * 0.25f / 5000f) * 5000f;
        public float CreditLineRate => LoanRate(12) + 0.02f;

        public string DrawCredit(float amount)
        {
            var cl = CreditLine;
            float used = cl?.balance ?? 0f;
            if (amount <= 0) return "Choose an amount";
            if (used + amount > CreditLineLimit + 1f) return $"Credit line limit is {K0(CreditLineLimit)}";
            if (amount > LoanHeadroom + 1f) return "That would exceed your total borrowing limit";
            if (cl == null)
            {
                cl = new Loan { id = S.NewId(), lender = "Credit line", creditLine = true, rate = CreditLineRate, nextHour = S.hour + Catalog.HoursPerMonth };
                S.loans.Add(cl);
            }
            cl.rate = CreditLineRate;
            cl.balance += amount;
            cl.principal += amount;
            S.money += amount;
            S.current.borrowed += amount;
            Log($"Drew {K0(amount)} on the credit line.", 0);
            return null;
        }

        /// <summary>Pays down a loan (all of it by default). Term loans charge a 1% early repayment fee.</summary>
        public string Repay(Loan l, float amount = -1)
        {
            if (!S.loans.Contains(l)) return "Unknown loan";
            if (amount < 0 || amount > l.balance) amount = l.balance;
            float fee = l.creditLine ? 0 : Mathf.Round(amount * 0.01f);
            if (S.money < amount + fee) return "Not enough money";
            S.money -= amount + fee;
            S.current.repaid += amount;
            S.current.interest += fee;
            l.balance -= amount;
            if (l.balance <= 0.5f)
            {
                S.loans.Remove(l);
                if (!l.creditLine) { S.loansRepaid++; Unlock("zero_debt"); }
                Log(l.creditLine ? "Credit line repaid." : $"Loan from {l.lender} repaid in full{(fee > 0 ? $" ({K0(fee)} early repayment fee)" : "")}.", 1);
            }
            else if (!l.creditLine)
            {
                // Same term, smaller balance: recompute the payment.
                l.payment = Mathf.Round(Payment(l.balance, l.rate, Mathf.Max(1, l.monthsLeft)));
                Log($"Paid {K0(amount)} off the {l.lender} loan. New payment {K0(l.payment)}/month.", 1);
            }
            return null;
        }

        /// <summary>Hourly: collects any loan payments that fall due.</summary>
        void LoansHour()
        {
            foreach (var l in S.loans.ToList())
            {
                if (S.hour < l.nextHour) continue;
                l.nextHour += Catalog.HoursPerMonth;
                float interest = Mathf.Round(l.balance * l.rate / 12f);
                float due = l.creditLine ? interest : Mathf.Min(l.payment, l.balance + interest);
                if (S.money < due && !S.cheatInfiniteMoney)
                {
                    // Missed: the interest is added to the balance and a late fee charged.
                    float fee = Mathf.Round(due * 0.05f);
                    l.balance += interest + fee;
                    S.missedPayments++;
                    S.creditScore = Mathf.Max(300, S.creditScore - 35);
                    Log($"Missed a {K0(due)} loan payment to {l.lender}! {K0(fee)} late fee added and your credit score dropped.", 3);
                    popups.Enqueue(new Popup { title = "Missed loan payment", body = $"You couldn't cover the {K0(due)} payment to {l.lender}. The unpaid interest and a late fee were added to the balance and your credit score fell.\n\nKeep enough cash for loan payments - check the Loans tab in Finance." }.Add("OK", null));
                    continue;
                }
                S.money -= due;
                S.current.interest += interest;
                float principal = due - interest;
                S.current.repaid += principal;
                l.balance -= principal;
                if (!l.creditLine)
                {
                    l.monthsLeft--;
                    if (l.monthsLeft <= 0 || l.balance <= 0.5f)
                    {
                        S.loans.Remove(l);
                        S.loansRepaid++;
                        Unlock("zero_debt");
                        Log($"Final payment made: the {l.lender} loan is paid off.", 1);
                    }
                }
            }
        }

        // ================================================================ insurance
        public static readonly string[] InsuranceTiers = { "None", "Basic", "Comprehensive" };

        public static string InsuranceDesc(int tier)
        {
            switch (tier)
            {
                case 1: return "Covers fire and flood damage: 60% of losses above a $10,000 deductible.";
                case 2: return "Covers fire, flood, coolant leaks and theft: 90% of losses above a $5,000 deductible.";
                default: return "No cover. Every loss comes out of your pocket.";
            }
        }

        /// <summary>Replacement value of everything the company owns.</summary>
        public float InsuredValue => S.sites.Sum(site => site.objects.Sum(o =>
            (Catalog.Facility(o.def)?.cost ?? 0) + o.devices.Sum(d => Catalog.Device(d.def)?.cost ?? 0)));

        /// <summary>Premium multiplier from how well protected the company is, with the reasons.</summary>
        public float InsuranceRisk(List<string> why)
        {
            float m = 1f;
            int halls = 0, suppressed = 0, raisedHalls = 0;
            foreach (var sim in company.sims)
                foreach (var z in sim.zones)
                {
                    if (!sim.Racks.Any(r => r.devices.Count > 0 && sim.ZoneAt(r) == z.id)) continue;
                    halls++;
                    if (z.suppressed) suppressed++;
                    if (z.floor > 0 || z.raisedFrac > 0.8f) raisedHalls++;
                }
            if (halls > 0)
            {
                float f = suppressed / (float)halls;
                m *= 1.25f - 0.4f * f;
                why?.Add($"x{1.25f - 0.4f * f:0.00} fire suppression in {suppressed}/{halls} occupied halls");
                float r = raisedHalls / (float)halls;
                m *= 1.1f - 0.2f * r;
                why?.Add($"x{1.1f - 0.2f * r:0.00} flood protection in {raisedHalls}/{halls} halls");
            }
            int sec = S.sites.Sum(site => site.staff.Count(st => st.role == "Security"));
            if (sec > 0) { m *= 0.92f; why?.Add("x0.92 security staff"); }
            bool access = S.sites.Any(site => site.objects.Any(o => Catalog.Facility(o.def)?.kind == FacilityKind.AccessControl));
            if (access) { m *= 0.95f; why?.Add("x0.95 access control"); }
            float risk = S.sites.Average(site => site.city.disasterRate);
            m *= 0.75f + 0.25f * risk;
            why?.Add($"x{0.75f + 0.25f * risk:0.00} local disaster risk");
            if (S.claimsRecent > 0.05f) { m *= 1f + 0.2f * S.claimsRecent; why?.Add($"x{1f + 0.2f * S.claimsRecent:0.00} recent claims"); }
            return m;
        }

        public float PremiumFor(int tier)
        {
            if (tier <= 0) return 0;
            float rate = tier == 1 ? 0.0011f : 0.002f;
            return Mathf.Round(InsuredValue * rate * InsuranceRisk(null) / 10f) * 10f;
        }

        [NonSerialized] float cachedPremium = -1;
        [NonSerialized] int cachedPremiumDay = -1;

        public float MonthlyPremium
        {
            get
            {
                if (cachedPremiumDay != S.Day || cachedPremium < 0) { cachedPremium = PremiumFor(S.insuranceTier); cachedPremiumDay = S.Day; }
                return cachedPremium;
            }
        }

        public void SetInsurance(int tier)
        {
            tier = Mathf.Clamp(tier, 0, 2);
            if (tier == S.insuranceTier) return;
            S.insuranceTier = tier;
            cachedPremium = -1;
            Log(tier == 0 ? "Cancelled your insurance policy." : $"Insurance policy: {InsuranceTiers[tier]} ({K0(PremiumFor(tier))}/month).", tier == 0 ? 2 : 1);
        }

        void InsuranceHour()
        {
            if (S.insuranceTier <= 0) return;
            float p = MonthlyPremium / Catalog.HoursPerMonth;
            S.money -= p;
            S.current.insurance += p;
        }

        static bool Covers(int tier, string peril) => tier == 2 || (tier == 1 && (peril == "fire" || peril == "flood"));

        /// <summary>Reports an insurable loss; pays out if the policy covers it.</summary>
        void InsuredLoss(string peril, float loss, string what)
        {
            if (loss <= 0) return;
            int tier = S.insuranceTier;
            if (!Covers(tier, peril))
            {
                if (tier == 0 && loss > 5000) Log($"Uninsured loss: {K0(loss)} ({what}). Insurance would have covered most of it.", 2);
                return;
            }
            float deductible = tier == 1 ? 10000 : 5000;
            float pay = Mathf.Round(Mathf.Max(0, loss - deductible) * (tier == 1 ? 0.6f : 0.9f));
            if (pay <= 0) { Log($"Loss of {K0(loss)} ({what}) was below your {K0(deductible)} deductible.", 0); return; }
            S.money += pay;
            S.current.claims += pay;
            S.claimsMade++;
            S.claimsPaidTotal += pay;
            S.claimsRecent += 1f;
            cachedPremium = -1;
            Unlock("insured");
            Log($"Insurance paid out {K0(pay)} for {what} (loss {K0(loss)}).", 1);
        }
    }
}
