package com.payroll.netpay.model;

import com.fasterxml.jackson.annotation.JsonProperty;

public class NetPayResult {
    @JsonProperty("grossPay")
    private double grossPay;

    @JsonProperty("federalTax")
    private double federalTax;

    @JsonProperty("stateTax")
    private double stateTax;

    @JsonProperty("additionalFederalWithholding")
    private double additionalFederalWithholding;

    @JsonProperty("additionalStateWithholding")
    private double additionalStateWithholding;

    @JsonProperty("totalTax")
    private double totalTax;

    @JsonProperty("totalFixedDeductions")
    private double totalFixedDeductions;

    @JsonProperty("totalPercentDeductions")
    private double totalPercentDeductions;

    @JsonProperty("totalDeductions")
    private double totalDeductions;

    @JsonProperty("netPay")
    private double netPay;

    @JsonProperty("payRate")
    private double payRate;

    @JsonProperty("payType")
    private String payType;

    @JsonProperty("totalHoursWorked")
    private double totalHoursWorked;

    @JsonProperty("payPeriodStart")
    private String payPeriodStart;

    @JsonProperty("payPeriodEnd")
    private String payPeriodEnd;

    @JsonProperty("employeeId")
    private String employeeId;

    @JsonProperty("payPeriodNumber")
    private long payPeriodNumber;

    public NetPayResult() {}

    public double getGrossPay() { return grossPay; }
    public void setGrossPay(double grossPay) { this.grossPay = grossPay; }

    public double getFederalTax() { return federalTax; }
    public void setFederalTax(double federalTax) { this.federalTax = federalTax; }

    public double getStateTax() { return stateTax; }
    public void setStateTax(double stateTax) { this.stateTax = stateTax; }

    public double getAdditionalFederalWithholding() { return additionalFederalWithholding; }
    public void setAdditionalFederalWithholding(double v) { this.additionalFederalWithholding = v; }

    public double getAdditionalStateWithholding() { return additionalStateWithholding; }
    public void setAdditionalStateWithholding(double v) { this.additionalStateWithholding = v; }

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

    public String getEmployeeId() { return employeeId; }
    public void setEmployeeId(String employeeId) { this.employeeId = employeeId; }

    public long getPayPeriodNumber() { return payPeriodNumber; }
    public void setPayPeriodNumber(long payPeriodNumber) { this.payPeriodNumber = payPeriodNumber; }
}
