package com.payroll.netpay;

import java.util.Map;

public class TaxCalculator {

    private static final int PAY_PERIODS_PER_YEAR = 26;

    // 2024 Federal progressive tax brackets
    // Each entry: {upper bound of bracket (annual), marginal rate}
    // Upper bound of Double.MAX_VALUE means "everything above"
    private static final double[][] SINGLE_BRACKETS = {
        {11600.0,    0.10},
        {47150.0,    0.12},
        {100525.0,   0.22},
        {191950.0,   0.24},
        {243725.0,   0.32},
        {609350.0,   0.35},
        {Double.MAX_VALUE, 0.37}
    };

    private static final double[][] MARRIED_BRACKETS = {
        {23200.0,    0.10},
        {94300.0,    0.12},
        {201050.0,   0.22},
        {383900.0,   0.24},
        {487450.0,   0.32},
        {731200.0,   0.35},
        {Double.MAX_VALUE, 0.37}
    };

    // Simplified flat state tax rates
    private static final Map<String, Double> STATE_RATES = Map.ofEntries(
        Map.entry("AL", 0.0500),
        Map.entry("AK", 0.0),
        Map.entry("AZ", 0.0250),
        Map.entry("AR", 0.0440),
        Map.entry("CA", 0.0930),
        Map.entry("CO", 0.0440),
        Map.entry("CT", 0.0500),
        Map.entry("DE", 0.0660),
        Map.entry("FL", 0.0),
        Map.entry("GA", 0.0549),
        Map.entry("HI", 0.0725),
        Map.entry("ID", 0.0580),
        Map.entry("IL", 0.0495),
        Map.entry("IN", 0.0305),
        Map.entry("IA", 0.0570),
        Map.entry("KS", 0.0570),
        Map.entry("KY", 0.0400),
        Map.entry("LA", 0.0425),
        Map.entry("ME", 0.0715),
        Map.entry("MD", 0.0575),
        Map.entry("MA", 0.0500),
        Map.entry("MI", 0.0425),
        Map.entry("MN", 0.0985),
        Map.entry("MS", 0.0500),
        Map.entry("MO", 0.0480),
        Map.entry("MT", 0.0675),
        Map.entry("NE", 0.0664),
        Map.entry("NV", 0.0),
        Map.entry("NH", 0.0),
        Map.entry("NJ", 0.1075),
        Map.entry("NM", 0.0590),
        Map.entry("NY", 0.0685),
        Map.entry("NC", 0.0450),
        Map.entry("ND", 0.0195),
        Map.entry("OH", 0.0350),
        Map.entry("OK", 0.0475),
        Map.entry("OR", 0.0990),
        Map.entry("PA", 0.0307),
        Map.entry("RI", 0.0599),
        Map.entry("SC", 0.0640),
        Map.entry("SD", 0.0),
        Map.entry("TN", 0.0),
        Map.entry("TX", 0.0),
        Map.entry("UT", 0.0465),
        Map.entry("VT", 0.0875),
        Map.entry("VA", 0.0575),
        Map.entry("WA", 0.0),
        Map.entry("WV", 0.0512),
        Map.entry("WI", 0.0530),
        Map.entry("WY", 0.0),
        Map.entry("DC", 0.0895)
    );

    /**
     * Calculate per-period federal tax using progressive brackets.
     * Annualizes bi-weekly gross pay, applies brackets, divides back to per-period.
     */
    public static double computeFederalTax(double biWeeklyGross, String filingStatus) {
        double annualIncome = biWeeklyGross * PAY_PERIODS_PER_YEAR;
        double[][] brackets = selectBrackets(filingStatus);
        double annualTax = applyBrackets(annualIncome, brackets);
        return roundToTwoDecimals(annualTax / PAY_PERIODS_PER_YEAR);
    }

    /**
     * Calculate per-period state tax using flat rate.
     * Annualizes bi-weekly gross pay, applies flat rate, divides back to per-period.
     */
    public static double computeStateTax(double biWeeklyGross, String state) {
        if (state == null || state.isEmpty()) return 0.0;
        double rate = STATE_RATES.getOrDefault(state.toUpperCase(), 0.0);
        double annualIncome = biWeeklyGross * PAY_PERIODS_PER_YEAR;
        double annualTax = annualIncome * rate;
        return roundToTwoDecimals(annualTax / PAY_PERIODS_PER_YEAR);
    }

    private static double[][] selectBrackets(String filingStatus) {
        if (filingStatus == null) return SINGLE_BRACKETS;
        return switch (filingStatus.toLowerCase()) {
            case "married", "marriedfilingjointly" -> MARRIED_BRACKETS;
            default -> SINGLE_BRACKETS; // Single, HeadOfHousehold, etc.
        };
    }

    private static double applyBrackets(double annualIncome, double[][] brackets) {
        double tax = 0.0;
        double prevBound = 0.0;
        for (double[] bracket : brackets) {
            double upperBound = bracket[0];
            double rate = bracket[1];
            if (annualIncome <= prevBound) break;
            double taxableInBracket = Math.min(annualIncome, upperBound) - prevBound;
            tax += taxableInBracket * rate;
            prevBound = upperBound;
        }
        return tax;
    }

    private static double roundToTwoDecimals(double value) {
        return Math.round(value * 100.0) / 100.0;
    }
}
