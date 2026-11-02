WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
SET DEFINE OFF

-- Run once against the empty schema. All seed changes are one transaction.
DECLARE
    TYPE name_list IS TABLE OF VARCHAR2(100);
    departments name_list := name_list(
        'Machining', 'Assembly', 'Fabrication', 'Welding', 'Painting',
        'Quality Control', 'Packaging', 'Electronics');
    products name_list := name_list(
        'Mounting Bracket', 'Drive Shaft', 'Gear Housing', 'Control Panel',
        'Bearing Assembly', 'Support Frame', 'Motor Mount', 'Valve Body',
        'Cable Harness', 'Protective Cover');
    product_ids SYS.ODCINUMBERLIST := SYS.ODCINUMBERLIST();
    department_ids SYS.ODCINUMBERLIST := SYS.ODCINUMBERLIST();
    new_id NUMBER;
    order_id NUMBER;
    quantity NUMBER;
    stage NUMBER;
    created_at TIMESTAMP WITH TIME ZONE;
    history_step NUMBER;
    existing_rows NUMBER;
    product_description VARCHAR2(500);
BEGIN
    SELECT (SELECT COUNT(*) FROM Departments) +
           (SELECT COUNT(*) FROM Products) +
           (SELECT COUNT(*) FROM ProductionOrders) +
           (SELECT COUNT(*) FROM OrderStatusHistory)
    INTO existing_rows FROM dual;
    IF existing_rows > 0 THEN
        RAISE_APPLICATION_ERROR(-20001, 'Mock data requires empty tables. No data changed.');
    END IF;

    FOR i IN 1..departments.COUNT LOOP
        INSERT INTO Departments (Name) VALUES (departments(i))
        RETURNING DepartmentId INTO new_id;
        department_ids.EXTEND;
        department_ids(i) := new_id;
    END LOOP;

    FOR i IN 1..40 LOOP
        product_description := products(MOD(i - 1, products.COUNT) + 1) ||
            ' - Variant ' || TO_CHAR(TRUNC((i - 1) / products.COUNT) + 1);
        INSERT INTO Products (PartNumber, Description)
        VALUES ('ATC-' || TO_CHAR(i, 'FM0000'),
            product_description)
        RETURNING ProductId INTO new_id;
        product_ids.EXTEND;
        product_ids(i) := new_id;
    END LOOP;

    FOR i IN 1..200 LOOP
        quantity := 25 * (MOD(i * 7, 20) + 1);
        stage := MOD(i - 1, 5);
        INSERT INTO ProductionOrders
            (ProductId, DepartmentId, QuantityOrdered, DueDate)
        VALUES (product_ids(MOD(i - 1, 40) + 1),
            department_ids(MOD(TRUNC((i - 1) / 5), 8) + 1),
            quantity, TRUNC(SYSDATE) + MOD(i * 13, 61) - 20)
        RETURNING OrderId INTO order_id;

        IF stage IN (1, 2, 3) THEN
            UPDATE ProductionOrders SET Status = 'Released' WHERE OrderId = order_id;
        END IF;
        IF stage IN (2, 3) THEN
            UPDATE ProductionOrders SET Status = 'In Progress',
                QuantityCompleted = TRUNC(quantity * 0.4) WHERE OrderId = order_id;
        END IF;
        IF stage = 3 THEN
            UPDATE ProductionOrders SET Status = 'Completed',
                QuantityCompleted = quantity WHERE OrderId = order_id;
        ELSIF stage = 4 THEN
            UPDATE ProductionOrders SET Status = 'Cancelled' WHERE OrderId = order_id;
        END IF;

        -- Give the generated mock history plausible dates in chronological order.
        created_at := SYSTIMESTAMP - NUMTODSINTERVAL(30 + MOD(i, 30), 'DAY');
        history_step := 0;
        FOR h IN (SELECT HistoryId FROM OrderStatusHistory
                  WHERE OrderId = order_id ORDER BY HistoryId) LOOP
            UPDATE OrderStatusHistory
            SET ChangedAt = created_at + NUMTODSINTERVAL(history_step * 2, 'DAY')
            WHERE HistoryId = h.HistoryId;
            history_step := history_step + 1;
        END LOOP;
    END LOOP;
END;
/
COMMIT;
