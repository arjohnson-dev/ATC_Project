WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
SET DEFINE OFF

CREATE TABLE Departments (
    DepartmentId NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    Name VARCHAR2(100 CHAR) NOT NULL UNIQUE
);

CREATE TABLE Products (
    ProductId NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    PartNumber VARCHAR2(40 CHAR) NOT NULL UNIQUE,
    Description VARCHAR2(500 CHAR) NOT NULL
);

CREATE TABLE ProductionOrders (
    OrderId NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ProductId NUMBER NOT NULL REFERENCES Products(ProductId),
    DepartmentId NUMBER NOT NULL REFERENCES Departments(DepartmentId),
    QuantityOrdered NUMBER(10) NOT NULL,
    QuantityCompleted NUMBER(10) DEFAULT 0 NOT NULL,
    DueDate DATE NOT NULL,
    Status VARCHAR2(20 CHAR) DEFAULT 'Planned' NOT NULL,
    CONSTRAINT ck_order_quantity CHECK (QuantityOrdered > 0),
    CONSTRAINT ck_completed_quantity CHECK (
        QuantityCompleted >= 0 AND QuantityCompleted <= QuantityOrdered),
    CONSTRAINT ck_order_status CHECK (
        Status IN ('Planned', 'Released', 'In Progress', 'Completed', 'Cancelled')),
    CONSTRAINT ck_completed_status CHECK (
        Status <> 'Completed' OR QuantityCompleted = QuantityOrdered)
);

CREATE TABLE OrderStatusHistory (
    HistoryId NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    OrderId NUMBER NOT NULL REFERENCES ProductionOrders(OrderId),
    PreviousStatus VARCHAR2(20 CHAR),
    NewStatus VARCHAR2(20 CHAR) NOT NULL,
    ChangedAt TIMESTAMP(6) WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT ck_history_previous CHECK (PreviousStatus IN (
        'Planned', 'Released', 'In Progress', 'Completed', 'Cancelled')),
    CONSTRAINT ck_history_new CHECK (NewStatus IN (
        'Planned', 'Released', 'In Progress', 'Completed', 'Cancelled')),
    CONSTRAINT ck_history_change CHECK (
        PreviousStatus IS NULL OR PreviousStatus <> NewStatus)
);

CREATE INDEX ix_orders_product ON ProductionOrders(ProductId);
CREATE INDEX ix_orders_department ON ProductionOrders(DepartmentId);
CREATE INDEX ix_orders_status_due ON ProductionOrders(Status, DueDate);
CREATE INDEX ix_history_order_time ON OrderStatusHistory(OrderId, ChangedAt);

CREATE OR REPLACE TRIGGER trg_order_status_history
AFTER INSERT OR UPDATE OF Status ON ProductionOrders
FOR EACH ROW
BEGIN
    IF INSERTING OR :OLD.Status <> :NEW.Status THEN
        INSERT INTO OrderStatusHistory (OrderId, PreviousStatus, NewStatus)
        VALUES (:NEW.OrderId, :OLD.Status, :NEW.Status);
    END IF;
END;
/
