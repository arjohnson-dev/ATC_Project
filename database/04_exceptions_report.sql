WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
SET DEFINE OFF

-- One reusable joined data set for report totals, grouping, and detail.
CREATE OR REPLACE VIEW vw_OrderReportBase AS
SELECT o.OrderId, o.ProductId, p.PartNumber, p.Description,
       o.DepartmentId, d.Name AS DepartmentName, o.QuantityOrdered,
       o.QuantityCompleted, o.DueDate, o.Status,
       o.QuantityOrdered - o.QuantityCompleted AS QuantityRemaining
FROM ProductionOrders o
JOIN Products p ON p.ProductId = o.ProductId
JOIN Departments d ON d.DepartmentId = o.DepartmentId;

CREATE OR REPLACE PROCEDURE usp_GetOrderExceptions (
    p_as_of IN DATE, p_department IN NUMBER, p_product IN NUMBER,
    p_status IN VARCHAR2, p_due_from IN DATE, p_due_to IN DATE,
    p_search IN VARCHAR2, p_offset IN NUMBER, p_page_size IN NUMBER,
    p_totals OUT SYS_REFCURSOR, p_departments OUT SYS_REFCURSOR,
    p_details OUT SYS_REFCURSOR
) AS
BEGIN
    -- Each cursor uses the same predicate. Caller opens a read-only transaction
    -- so totals, department groups, and details share one database snapshot.
    OPEN p_totals FOR
    WITH filtered AS (
        SELECT b.*,
          CASE WHEN Status NOT IN ('Completed','Cancelled') AND QuantityRemaining > 0 THEN 1 ELSE 0 END IsOpen,
          CASE WHEN Status NOT IN ('Completed','Cancelled') AND QuantityRemaining > 0
               AND DueDate < TRUNC(p_as_of) THEN 1 ELSE 0 END IsOverdue
        FROM vw_OrderReportBase b
        WHERE (p_department IS NULL OR DepartmentId=p_department)
          AND (p_product IS NULL OR ProductId=p_product)
          AND (p_status IS NULL OR Status=p_status)
          AND (p_due_from IS NULL OR DueDate>=TRUNC(p_due_from))
          AND (p_due_to IS NULL OR DueDate<=TRUNC(p_due_to)+(86399/86400))
          AND (p_search IS NULL OR UPPER(PartNumber) LIKE p_search ESCAPE '\' OR UPPER(Description) LIKE p_search ESCAPE '\')
    )
    SELECT NVL(SUM(IsOverdue),0),
           NVL(SUM(CASE WHEN IsOverdue=1 THEN QuantityOrdered ELSE 0 END),0),
           NVL(SUM(CASE WHEN IsOverdue=1 THEN QuantityCompleted ELSE 0 END),0),
           NVL(SUM(CASE WHEN IsOverdue=1 THEN QuantityRemaining ELSE 0 END),0),
           NVL(SUM(IsOpen),0),
           NVL(SUM(CASE WHEN IsOpen=1 THEN QuantityRemaining ELSE 0 END),0)
    FROM filtered;

    OPEN p_departments FOR
    SELECT DepartmentId, DepartmentName, COUNT(*), SUM(QuantityRemaining)
    FROM vw_OrderReportBase
    WHERE Status NOT IN ('Completed','Cancelled') AND QuantityRemaining>0 AND DueDate<TRUNC(p_as_of)
      AND (p_department IS NULL OR DepartmentId=p_department)
      AND (p_product IS NULL OR ProductId=p_product)
      AND (p_status IS NULL OR Status=p_status)
      AND (p_due_from IS NULL OR DueDate>=TRUNC(p_due_from))
      AND (p_due_to IS NULL OR DueDate<=TRUNC(p_due_to)+(86399/86400))
      AND (p_search IS NULL OR UPPER(PartNumber) LIKE p_search ESCAPE '\' OR UPPER(Description) LIKE p_search ESCAPE '\')
    GROUP BY DepartmentId, DepartmentName ORDER BY DepartmentName;

    OPEN p_details FOR
    SELECT OrderId, ProductId, PartNumber, Description, DepartmentId, DepartmentName,
           QuantityOrdered, QuantityCompleted, DueDate, Status, QuantityRemaining,
           1 AS IsOverdue, TRUNC(p_as_of)-TRUNC(DueDate) AS DaysOverdue
    FROM vw_OrderReportBase
    WHERE Status NOT IN ('Completed','Cancelled') AND QuantityRemaining>0 AND DueDate<TRUNC(p_as_of)
      AND (p_department IS NULL OR DepartmentId=p_department)
      AND (p_product IS NULL OR ProductId=p_product)
      AND (p_status IS NULL OR Status=p_status)
      AND (p_due_from IS NULL OR DueDate>=TRUNC(p_due_from))
      AND (p_due_to IS NULL OR DueDate<=TRUNC(p_due_to)+(86399/86400))
      AND (p_search IS NULL OR UPPER(PartNumber) LIKE p_search ESCAPE '\' OR UPPER(Description) LIKE p_search ESCAPE '\')
    ORDER BY DueDate, OrderId
    OFFSET p_offset ROWS FETCH NEXT NVL(p_page_size, 999999999999) ROWS ONLY;
END;
/
SHOW ERRORS PROCEDURE usp_GetOrderExceptions
DECLARE
    error_count NUMBER;
BEGIN
    SELECT COUNT(*) INTO error_count FROM all_errors
    WHERE owner=SYS_CONTEXT('USERENV','CURRENT_SCHEMA')
      AND name='USP_GETORDEREXCEPTIONS' AND type='PROCEDURE';
    IF error_count > 0 THEN
        RAISE_APPLICATION_ERROR(-20004, 'Report procedure has compilation errors.');
    END IF;
END;
/
