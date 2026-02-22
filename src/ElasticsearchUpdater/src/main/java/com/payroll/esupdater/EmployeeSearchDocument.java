package com.payroll.esupdater;

import com.fasterxml.jackson.annotation.JsonProperty;

import java.util.ArrayList;
import java.util.List;

public class EmployeeSearchDocument {

    @JsonProperty("employee_id")
    private String employeeId;

    @JsonProperty("first_name")
    private String firstName;

    @JsonProperty("last_name")
    private String lastName;

    @JsonProperty("email")
    private String email;

    @JsonProperty("pay_type")
    private String payType;

    @JsonProperty("pay_rate")
    private double payRate;

    @JsonProperty("pay_period_hours")
    private double payPeriodHours;

    @JsonProperty("is_active")
    private boolean isActive;

    @JsonProperty("hire_date")
    private String hireDate;

    @JsonProperty("pay_periods")
    private List<PayPeriodRecord> payPeriods = new ArrayList<>();

    public EmployeeSearchDocument() {}

    public String getEmployeeId() { return employeeId; }
    public void setEmployeeId(String employeeId) { this.employeeId = employeeId; }

    public String getFirstName() { return firstName; }
    public void setFirstName(String firstName) { this.firstName = firstName; }

    public String getLastName() { return lastName; }
    public void setLastName(String lastName) { this.lastName = lastName; }

    public String getEmail() { return email; }
    public void setEmail(String email) { this.email = email; }

    public String getPayType() { return payType; }
    public void setPayType(String payType) { this.payType = payType; }

    public double getPayRate() { return payRate; }
    public void setPayRate(double payRate) { this.payRate = payRate; }

    public double getPayPeriodHours() { return payPeriodHours; }
    public void setPayPeriodHours(double payPeriodHours) { this.payPeriodHours = payPeriodHours; }

    public boolean isActive() { return isActive; }
    public void setActive(boolean active) { isActive = active; }

    public String getHireDate() { return hireDate; }
    public void setHireDate(String hireDate) { this.hireDate = hireDate; }

    public List<PayPeriodRecord> getPayPeriods() { return payPeriods; }
    public void setPayPeriods(List<PayPeriodRecord> payPeriods) { this.payPeriods = payPeriods; }
}
