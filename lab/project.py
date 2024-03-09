import math

x=float(input("X'i daxil edin:"))
eps=0.001
a=1
y=1
z=0
while abs(y)>eps:
    y=((-1)*(a+1))*((math.sin(x)**a)/math.factorial(a))
    a+=1
    z+=y
print(z)
