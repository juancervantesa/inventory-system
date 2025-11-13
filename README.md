# Sistema de Inventario con Arquitectura de Microservicios y Kafka

Este proyecto implementa un sistema de inventario básico utilizando una arquitectura de microservicios, orquestada con Docker Compose. La comunicación entre servicios se realiza de forma asíncrona a través de un clúster de Apache Kafka para garantizar la resiliencia y la consistencia de los datos.

## Arquitectura

El sistema se compone de los siguientes servicios:

-   **Products Service**: Gestiona la información de los productos, incluido el stock.
-   **Orders Service**: Gestiona la creación de pedidos.
-   **Load Balancer**: Un balanceador de carga Nginx que distribuye el tráfico entre las instancias del `Products Service`.
-   **Bases de datos PostgreSQL**: Dos bases de datos aisladas, una para cada servicio (`products-db` y `orders-db`).
-   **Clúster de Kafka**: Utilizado para la comunicación asíncrona entre servicios.
    -   **Zookeeper**: Requerido para la gestión del clúster de Kafka.
    -   **Kafka**: El broker de mensajes que almacena los eventos.
    -   **Kafka UI**: Una interfaz de usuario web para visualizar y gestionar el clúster de Kafka.

---

## Implementación de Arquitectura Orientada a Eventos con Kafka

Para resolver problemas de consistencia de datos y mejorar el desacoplamiento entre los servicios, se ha implementado una comunicación basada en eventos.

### Problema Original: Condición de Carrera

En la comunicación síncrona inicial, el `Orders Service` primero consultaba el stock del `Products Service` y luego creaba el pedido. Este enfoque de "verificar y luego actuar" es propenso a condiciones de carrera: dos pedidos podían verificar el stock simultáneamente y ser procesados, incluso si el stock combinado superaba el disponible.

### Solución: Comunicación Asíncrona

Se reemplazo la comunicación síncrona con actualización de stock síncrona por un flujo de eventos asíncrono, que garantiza que las actualizaciones de inventario sean atómicas y se procesen en orden.

El nuevo flujo funciona de la siguiente manera:

1.  **Creación del Pedido**: Un cliente envía una solicitud `POST` al `Orders Service` para crear un nuevo pedido.
2.  **Validación y Publicación del Evento**:
    -   El `Orders Service` realiza una validación inicial (ej. que el producto exista).
    -   Guarda el nuevo pedido en su propia base de datos.
    -   Publica un evento `OrderPlaced` en el topic de Kafka `orders_topic`. El mensaje contiene el `ProductId` y la `Quantity` del pedido.
3.  **Consumo del Evento y Actualización de Stock**:
    -   El `Products Service` está suscrito al topic `orders_topic`.
    -   Al recibir un mensaje `OrderPlaced`, el servicio consume el evento.
    -   Procede a **actualizar el stock** del producto correspondiente en su propia base de datos de forma atómica (`stock = stock - quantity`).

Este patrón  asegura que la responsabilidad de actualizar el stock recaiga únicamente en el `Products Service`, eliminando la condición de carrera y haciendo el sistema más robusto.

---

## Cómo Ejecutar el Proyecto

### Prerrequisitos

-   Tener Docker y Docker Compose instalados.

### Pasos para la Ejecución

1.  Clona este repositorio en tu máquina local.
2.  Abre una terminal en la raíz del proyecto.
3.  Ejecuta el siguiente comando para levantar todos los servicios en modo detached:

    ```shell
    docker compose up -d
    ```

### Acceso a los Servicios

Una vez que los contenedores estén en funcionamiento, puedes acceder a los diferentes componentes del sistema:

-   **API de Productos**: `http://localhost:8082/api/products` (a través del balanceador de carga)
-   **API de Pedidos**: `http://localhost:8083/api/orders`
-   **Interfaz de Usuario de Kafka**: `http://localhost:8090`

---

## Cómo Probar el Flujo de Eventos

Para verificar que la comunicación por eventos funciona correctamente, sigue estos pasos:

1.  **Crea un Producto**:
    Envía una petición `POST` a `http://localhost:8082/api/products` con el siguiente cuerpo:
    ```json
    {
        "name": "Laptop Pro",
        "stock": 100
    }
    ```
    *Nota: La creación de un producto **no** genera un evento en Kafka.*

2.  **Crea una Orden**:
    Envía una petición `POST` a `http://localhost:8083/api/orders` para comprar el producto recién creado (asegúrate de usar el `id` correcto).
    ```json
    {
        "productId": 1,
        "quantity": 5
    }
    ```

3.  **Verifica el Mensaje en Kafka UI**:
    -   Abre la interfaz de Kafka UI en `http://localhost:8090`.
    -   Navega a la sección de "Topics" y selecciona `orders_topic`.
    -   En la pestaña "Messages", verás el evento que se acaba de generar para el pedido.

4.  **Verifica la Actualización del Stock**:
    Envía una petición `GET` a `http://localhost:8082/api/products/1`. El stock del producto ahora debería ser `95`.