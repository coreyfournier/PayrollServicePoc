package com.payroll.netpay.model;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;

@JsonIgnoreProperties(ignoreUnknown = true)
public class GrossPay {
    @JsonProperty("EMPLOYEE_ID")
    private String employeeId;

    @JsonProperty("PAY_PERIOD_NUMBER")
    private long payPeriodNumber;

    @JsonProperty("PAY_RATE")
    private double payRate;

    @JsonProperty("PAY_TYPE")
    private String payType;

    @JsonProperty("GROSS_PAY")
    private double grossPay;

    @JsonProperty("TOTAL_HOURS_WORKED")
    private double totalHoursWorked;

    @JsonProperty("PAY_PERIOD_START")
    private String payPeriodStart;

    @JsonProperty("PAY_PERIOD_END")
    private String payPeriodEnd;

    public GrossPay() {}

    public String getEmployeeId() { return employeeId; }
    public void setEmployeeId(String employeeId) { this.employeeId = employeeId; }

    public long getPayPeriodNumber() { return payPeriodNumber; }
    public void setPayPeriodNumber(long payPeriodNumber) { this.payPeriodNumber = payPeriodNumber; }

    public double getPayRate() { return payRate; }
    public void setPayRate(double payRate) { this.payRate = payRate; }

    public String getPayType() { return payType; }
    public void setPayType(String payType) { this.payType = payType; }

    public double getGrossPay() { return grossPay; }
    public void setGrossPay(double grossPay) { this.grossPay = grossPay; }

    public double getTotalHoursWorked() { return totalHoursWorked; }
    public void setTotalHoursWorked(double totalHoursWorked) { this.totalHoursWorked = totalHoursWorked; }

    public String getPayPeriodStart() { return payPeriodStart; }
    public void setPayPeriodStart(String payPeriodStart) { this.payPeriodStart = payPeriodStart; }

    public String getPayPeriodEnd() { return payPeriodEnd; }
    public void setPayPeriodEnd(String payPeriodEnd) { this.payPeriodEnd = payPeriodEnd; }
}
