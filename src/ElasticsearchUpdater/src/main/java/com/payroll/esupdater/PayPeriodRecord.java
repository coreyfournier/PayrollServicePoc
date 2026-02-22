package com.payroll.esupdater;

import com.fasterxml.jackson.annotation.JsonProperty;

public class PayPeriodRecord {

    @JsonProperty("pay_period_number")
    private long payPeriodNumber;

    @JsonProperty("gross_pay")
    private double grossPay;

    @JsonProperty("federal_tax")
    private double federalTax;

    @JsonProperty("state_tax")
    private double stateTax;

    @JsonProperty("additional_federal_withholding")
    private double additionalFederalWithholding;

    @JsonProperty("additional_state_withholding")
    private double additionalStateWithholding;

    @JsonProperty("total_tax")
    private double totalTax;

    @JsonProperty("total_fixed_deductions")
    private double totalFixedDeductions;

    @JsonProperty("total_percent_deductions")
    private double totalPercentDeductions;

    @JsonProperty("total_deductions")
    private double totalDeductions;

    @JsonProperty("net_pay")
    private double netPay;

    @JsonProperty("pay_rate")
    private double payRate;

    @JsonProperty("pay_type")
    private String payType;

    @JsonProperty("total_hours_worked")
    private double totalHoursWorked;

    @JsonProperty("pay_period_start")
    private String payPeriodStart;

    @JsonProperty("pay_period_end")
    private String payPeriodEnd;

    public PayPeriodRecord() {}

    public long getPayPeriodNumber() { return payPeriodNumber; }
    public void setPayPeriodNumber(long payPeriodNumber) { this.payPeriodNumber = payPeriodNumber; }

    public double getGrossPay() { return grossPay; }
    public void setGrossPay(double grossPay) { this.grossPay = grossPay; }

    public double getFederalTax() { return federalTax; }
    public void setFederalTax(double federalTax) { this.federalTax = federalTax; }

    public double getStateTax() { return stateTax; }
    public void setStateTax(double stateTax) { this.stateTax = stateTax; }

    public double getAdditionalFederalWithholding() { return additionalFederalWithholding; }
    public void setAdditionalFederalWithholding(double additionalFederalWithholding) { this.additionalFederalWithholding = additionalFederalWithholding; }

    public double getAdditionalStateWithholding() { return additionalStateWithholding; }
    public void setAdditionalStateWithholding(double additionalStateWithholding) { this.additionalStateWithholding = additionalStateWithholding; }

    public double getTotalTax() { return totalTax; }
    public void setTotalTax(double totalTax) { this.totalTax = totalTax; }

    public double getTotalFixedDeductions() { return totalFixedDeductions; }
    public void setTotalFixedDeductions(double totalFixedDeductions) { this.totalFixedDeductions = totalFixedDeductions; }

    public double getTotalPercentDeductions() { return totalPercentDeductions; }
    public void setTotalPercentDeductions(double totalPercentDeductions) { this.totalPercentDeductions = totalPercentDeductions; }

    public double getTotalDeductions() { return totalDeductions; }
    public void setTotalDeductions(double totalDeductions) { this.totalDeductions = totalDeductions; }

    public double getNetPay() { return netPay; }
    public void setNetPay(double netPay) { this.netPay = netPay; }

    public double getPayRate() { return payRate; }
    public void setPayRate(double payRate) { this.payRate = payRate; }

    public String getPayType() { return payType; }
    public void setPayType(String payType) { this.payType = payType; }

    public double getTotalHoursWorked() { return totalHoursWorked; }
    public void setTotalHoursWorked(double totalHoursWorked) { this.totalHoursWorked = totalHoursWorked; }

    public String getPayPeriodStart() { return payPeriodStart; }
    public void setPayPeriodStart(String payPeriodStart) { this.payPeriodStart = payPeriodStart; }

    public String getPayPeriodEnd() { return payPeriodEnd; }
    public void setPayPeriodEnd(String payPeriodEnd) { this.payPeriodEnd = payPeriodEnd; }
}
