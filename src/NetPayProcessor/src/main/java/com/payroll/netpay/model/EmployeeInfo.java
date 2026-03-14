package com.payroll.netpay.model;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;

@JsonIgnoreProperties(ignoreUnknown = true)
public class EmployeeInfo {
    @JsonProperty("employeeId")
    private String employeeId;

    @JsonProperty("payRate")
    private double payRate;

    @JsonProperty("payType")
    private String payType;

    @JsonProperty("payPeriodHours")
    private double payPeriodHours;

    public EmployeeInfo() {}

    public EmployeeInfo(String employeeId, double payRate, String payType, double payPeriodHours) {
        this.employeeId = employeeId;
        this.payRate = payRate;
        this.payType = payType;
        this.payPeriodHours = payPeriodHours;
    }

    public String getEmployeeId() { return employeeId; }
    public void setEmployeeId(String employeeId) { this.employeeId = employeeId; }

    public double getPayRate() { return payRate; }
    public void setPayRate(double payRate) { this.payRate = payRate; }

    public String getPayType() { return payType; }
    public void setPayType(String payType) { this.payType = payType; }

    public double getPayPeriodHours() { return payPeriodHours; }
    public void setPayPeriodHours(double payPeriodHours) { this.payPeriodHours = payPeriodHours; }
}
