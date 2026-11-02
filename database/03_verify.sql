WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
SET LINESIZE 180
SET PAGESIZE 100
SELECT 'Departments' AS TableName, COUNT(*) AS RowCount FROM Departments
UNION ALL SELECT 'Products', COUNT(*) FROM Products
UNION ALL SELECT 'ProductionOrders', COUNT(*) FROM ProductionOrders
UNION ALL SELECT 'OrderStatusHistory', COUNT(*) FROM OrderStatusHistory;

SELECT Status, COUNT(*) AS OrderCount,
    SUM(QuantityOrdered) AS Ordered, SUM(QuantityCompleted) AS Completed
FROM ProductionOrders GROUP BY Status ORDER BY Status;

SELECT o.OrderId, p.PartNumber, d.Name AS Department,
    o.QuantityOrdered, o.QuantityCompleted, TO_CHAR(o.DueDate, 'YYYY-MM-DD') AS DueDate,
    o.Status
FROM ProductionOrders o
JOIN Products p ON p.ProductId = o.ProductId
JOIN Departments d ON d.DepartmentId = o.DepartmentId
ORDER BY o.OrderId FETCH FIRST 10 ROWS ONLY;
