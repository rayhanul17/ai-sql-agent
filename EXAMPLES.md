# Example queries (copy‑paste to test)

Two ways to ask: **natural language** (English / Bangla / Banglish) or a
**direct SQL** query pasted straight in. Pick a provider + model in Settings
(gear / menu), and for MySQL/SQL Server pick the dialect and paste a connection
string first.

- **Demo DB** = the seeded PostgreSQL database that ships with the project and
  comes up automatically with `docker compose up`. Tables:
  `students, teachers, classes, attendance, fees, leaves`. Everything works
  against this out of the box — no extra setup.
- **classicmodels** *(optional)* = a MySQL sample DB. You only have this if you
  install it and paste its connection string yourself; the classicmodels examples
  below are skippable if you haven't. Tables: `customers, employees, offices,
  products, orders, orderdetails, payments, productlines`.

> Tip: small models (3B) do great on everyday questions; big meta‑questions and
> polished Bangla are better on 7B or the Groq cloud models.

---

## 1. Natural‑language — Demo DB (PostgreSQL)

### English
```
how many students are there
list all students
how many students in each class
top 5 students by fees
which students were absent
total fees collected
how many rows in each table
what tables are there
teachers ordered by salary
unpaid fees this month
```

### Bangla (Bengali script)
```
কতজন ছাত্র আছে
সব ছাত্রের তালিকা দাও
প্রতিটি ক্লাসে কতজন ছাত্র
সবচেয়ে বেশি ফি দেওয়া ৫ জন ছাত্র
কোন শিক্ষকের বেতন সবচেয়ে বেশি
এই মাসে কারা অনুপস্থিত ছিল
মোট কত ফি জমা হয়েছে
```

### Banglish (Bangla in Latin letters)
```
koto jon student ache
protiti class e koto jon student
sobcheye beshi fee deya 5 jon student
kon teacher er salary sobcheye beshi
ei mase ke ke absent chilo
mot koto fee joma hoyeche
```

---

## 2. Natural‑language — classicmodels (MySQL)

Connection string (change the credentials to yours):
```
Server=localhost;Port=3306;Database=classicmodels;User ID=root;Password=your_password;SslMode=None
```

### English
```
how many customers are there
customers per country
top 5 products by price
how many orders per status
total payments per customer
which products are low in stock
list employees and their office city
what tables are there
average order value
top 10 customers by total payments
```

### Bangla
```
কতজন গ্রাহক আছে
প্রতিটি দেশে কতজন গ্রাহক
দাম অনুযায়ী শীর্ষ ৫টি পণ্য
প্রতিটি স্ট্যাটাসে কতগুলো অর্ডার
সবচেয়ে বেশি পেমেন্ট করা ১০ জন গ্রাহক
```

### Banglish
```
koto jon customer ache
protiti desh e koto customer
dam onujayi top 5 product
kon product er stock kom
sobcheye beshi payment kora customer
```

### Follow‑ups (test conversation memory — ask right after a query)
```
banglay dao
as a chart
only the top 3
```

### Greeting / non‑data (should NOT run a query)
```
hi
thanks
ke tumi
```

---

## 3. Direct SQL — PostgreSQL (Demo DB)

Paste a SQL statement directly; it is validated (read‑only) and run as‑is.

### Correct
```sql
SELECT * FROM students;
```
```sql
SELECT c.name AS class_name, COUNT(s.id) AS students
FROM classes c LEFT JOIN students s ON s.class_id = c.id
GROUP BY c.name ORDER BY students DESC;
```
```sql
SELECT status, COUNT(*) FROM attendance GROUP BY status;
```
```sql
SELECT name, salary FROM teachers ORDER BY salary DESC LIMIT 5;
```

### Partially wrong (the DB errors, then the agent auto‑retries once)
```sql
-- wrong column name (should be "name"), agent should self‑correct
SELECT student_name FROM students;
```
```sql
-- wrong table name (should be "attendance")
SELECT * FROM attendence;
```
```sql
-- missing GROUP BY for the aggregate
SELECT class_id, COUNT(*) FROM students;
```

### Should be rejected by the safety layer (read‑only)
```sql
DELETE FROM students;
```
```sql
UPDATE fees SET paid = true;
```
```sql
DROP TABLE students;
```
```sql
SELECT * FROM students; DROP TABLE teachers;
```

---

## 4. Direct SQL — MySQL (classicmodels)

### Correct
```sql
SELECT COUNT(*) FROM customers;
```
```sql
SELECT country, COUNT(*) AS num_customers
FROM customers GROUP BY country ORDER BY num_customers DESC;
```
```sql
SELECT productName, MSRP FROM products ORDER BY MSRP DESC LIMIT 5;
```
```sql
SELECT o.status, COUNT(*) AS orders
FROM orders o GROUP BY o.status;
```
```sql
SELECT c.customerName, SUM(p.amount) AS total_paid
FROM customers c JOIN payments p ON p.customerNumber = c.customerNumber
GROUP BY c.customerName ORDER BY total_paid DESC LIMIT 10;
```

### Partially wrong (auto‑retry)
```sql
-- wrong column (should be customerName)
SELECT customer_name FROM customers;
```
```sql
-- wrong table (should be orderdetails)
SELECT * FROM order_details;
```

### Should be rejected (read‑only)
```sql
UPDATE products SET MSRP = 0;
```
```sql
TRUNCATE TABLE orders;
```
